# Anomaly Detection Flow

AegisAPI separates feature extraction from anomaly detection so that different detectors can consume the same event stream.

## 1. Feature Extraction
- `FeatureCollectorMiddleware` is registered at the start of the ASP.NET pipeline but invokes `_next` before collecting features, allowing it to observe the final response.
- For every request it collects the client id, **HTTP method**, **normalized route** (first two segments), request-rate window, user-agent entropy, status code, schema-error flag, and WAF hit flag.
- These values are packaged into a `RequestFeature` record and pushed to an in-memory queue.

## 2. In-memory Queue
- `RequestFeatureQueue` exposes a bounded `Channel<RequestFeature>`.
- Capacity is configured via `AnomalyDetectionSettings.FeatureQueueCapacity` and the queue drops the oldest item when full.
- Both the middleware and the detector service use this queue to decouple producers from consumers.

## 3. Detectors
`AnomalyDetectionService` pulls features from the queue and delegates to a detector implementation selected by `AnomalyDetectionSettings.Mode`.
Each anomaly increments an OpenTelemetry counter (`aegis.anomalies`) tagged with the reason and detector type and is logged at information level.

### Rolling Thresholds
`RollingThresholdDetector` keeps per-(client, route) windows of request timestamps in an `IMemoryCache` with idle expiration to evict inactive keys.
It flags anomalies when configured thresholds are exceeded for
  - requests per second (RPS),
  - 4xx/5xx errors,
  - WAF hits,
  - or when the user-agent entropy drops below a minimum.

### ML.NET (optional)
`MlAnomalyDetector` warms up by buffering only non-anomalous events until `BaselineSampleSize` is reached.
It trains an Isolation Forest (or Randomized PCA) pipeline with mean-variance normalization and derives a score threshold from the `ScoreQuantile` of training scores.
During active detection, it scores `[RPS, 4xx, 5xx, WAF, UA entropy, method]` vectors using a thread-safe prediction-engine pool; scores above the threshold are reported as `ml_outlier` anomalies.
A `PeriodicTimer` retrains the model every `RetrainIntervalMinutes` on recent buffered events with anti-reentrancy guards, and the model plus threshold are persisted to disk so a restart can skip warm-up.

### Hybrid
- `HybridDetector` applies rolling thresholds first and only invokes the ML detector if the rules consider the event nominal.

## 4. Summarizer Integration
When a detector flags an anomaly, `FeatureConsumerService` builds an `IncidentBundle` and calls the Summarizer service (`POST /ai/summarize`).
The bundle is redacted to remove tokens, API keys, emails, and raw IPs before being sent.
The Summarizer returns a `SummaryResponse` with a human-readable summary, probable cause, optional `PolicyPatch`, confidence score, and suggested next steps. The gateway logs this response and can surface patches for review.

## 5. Configuration
Key settings are provided via `AnomalyDetectionSettings`:
- `Mode` – selects Rules, Ml, or Hybrid detection.
- `RpsWindowSeconds`, `ErrorWindowSeconds`, `RpsThreshold`, `FourXxThreshold`, `FiveXxThreshold`, `WafThreshold`, `UaEntropyThreshold`.
- `FeatureQueueCapacity` for the bounded queue.
- `BaselineSampleSize`, `TrainingWindowMinutes`, `RetrainIntervalMinutes`, `ScoreQuantile`, `MinSamplesGuard`, `MinVarianceGuard` for the ML detector.

## 6. Tests
`tests/Gateway.IntegrationTests/AnomalyDetectionTests.cs` feeds a labeled sequence of request features to the detectors and asserts the expected anomalies for rule spikes, ML warm-up, and hybrid precedence.
