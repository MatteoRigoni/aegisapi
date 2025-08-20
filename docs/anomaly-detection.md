# Anomaly Detection Flow

AegisAPI separates feature extraction from anomaly detection so that different detectors can consume the same event stream.

## 1. Feature Extraction
- `FeatureCollectorMiddleware` runs at the end of the ASP.NET pipeline.
- For every request it collects the client id, request-rate window, user-agent entropy, route, status code, schema-error flag, and WAF hit flag.
- These values are packaged into a `RequestFeature` record and pushed to an in-memory queue.

## 2. In-memory Queue
- `RequestFeatureQueue` exposes a bounded `Channel<RequestFeature>`.
- Capacity is configured via `AnomalyDetectionSettings.FeatureQueueCapacity` and the queue drops the oldest item when full.
- Both the middleware and the detector service use this queue to decouple producers from consumers.

## 3. Detectors
`AnomalyDetectionService` pulls features from the queue and delegates to a detector implementation selected by `AnomalyDetectionSettings.Mode`:

### Rolling Thresholds
- `RollingThresholdDetector` keeps per-(client, route) windows of request timestamps.
- It flags anomalies when configured thresholds are exceeded for
  - requests per second (RPS),
  - 4xx/5xx errors,
  - WAF hits,
  - or when the user-agent entropy drops below a minimum.

### ML.NET (optional)
- `MlAnomalyDetector` warms up by buffering only non-anomalous events until `BaselineSampleSize` is reached.
- It trains a Randomized PCA model and derives a score threshold from the `ScoreQuantile` of training scores.
- During active detection, it scores `[RPS, 4xx, 5xx, WAF]` vectors; scores above the threshold are reported as anomalies.
- A background timer retrains the model every `RetrainIntervalMinutes` using recent buffered events, guarded by `MinSamplesGuard` and `MinVarianceGuard` to avoid unstable models.

### Hybrid
- `HybridDetector` applies rolling thresholds first and only invokes the ML detector if the rules consider the event nominal.

## 4. Configuration
Key settings are provided via `AnomalyDetectionSettings`:
- `Mode` – selects Rules, Ml, or Hybrid detection.
- `RpsWindowSeconds`, `ErrorWindowSeconds`, `RpsThreshold`, `FourXxThreshold`, `FiveXxThreshold`, `WafThreshold`, `UaEntropyThreshold`.
- `FeatureQueueCapacity` for the bounded queue.
- `BaselineSampleSize`, `TrainingWindowMinutes`, `RetrainIntervalMinutes`, `ScoreQuantile`, `MinSamplesGuard`, `MinVarianceGuard` for the ML detector.

## 5. Tests
`tests/Gateway.IntegrationTests/AnomalyDetectionTests.cs` feeds a labeled sequence of request features to the detectors and asserts the expected anomalies for rule spikes, ML warm-up, and hybrid precedence.
