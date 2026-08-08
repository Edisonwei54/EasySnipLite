using System.Windows;

namespace EasySnipLite.Editor;

/// <summary>文字标注输入框：确定后经 Text 属性返回输入内容。</summary>
public partial class TextInputDialog : Window
{
    public string Text => Input.Text;

    public TextInputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Input.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
