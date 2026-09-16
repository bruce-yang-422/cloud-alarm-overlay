using System.Security.Cryptography;
namespace CloudAlarmOverlay.Core.Services;
internal sealed class AckCodeGenerator : IAckCodeGenerator
{
    public string Generate()
    {
        const string digits="0123456789";
        var chars=Enumerable.Range(0,9).Select(_=>digits[RandomNumberGenerator.GetInt32(digits.Length)]).ToArray();
        return new string(chars,0,3)+"-"+new string(chars,3,3)+"-"+new string(chars,6,3);
    }
}
