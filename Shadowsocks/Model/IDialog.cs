namespace Shadowsocks.Model
{
    public enum DialogResult
    {
        OK, Yes, No
    }

    public interface IDialog
    {
        void Show(string message);

        DialogResult Ask(string message);
    }
}
