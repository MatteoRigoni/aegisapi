using System.Text.RegularExpressions;

namespace Summarizer.Security;

public static class Redactor
{
    static readonly Regex Bearer = new(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex ApiKey = new(@"(?i)(x-api-key\s*:\s*)([A-Za-z0-9\-_]{8,})", RegexOptions.Compiled);
    static readonly Regex Email  = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex Ip     = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);

    public static string Mask(string s)
    {           
        if (string.IsNullOrEmpty(s)) return s;
        s = Bearer.Replace(s, "Bearer ***");
        s = ApiKey.Replace(s, "$1***");
        s = Email.Replace(s, "***@***");
        s = Ip.Replace(s, "***.***.***.***");
        return s;
    }
}
