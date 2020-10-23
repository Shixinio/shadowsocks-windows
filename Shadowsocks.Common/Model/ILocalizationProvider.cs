namespace Shadowsocks.Common.Model
{
    public interface ILocalizationProvider
    {
        T GetLocalizedValue<T>(string key);
    }
}
