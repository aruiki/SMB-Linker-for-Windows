using Microsoft.UI.Xaml.Markup;

namespace SMBLinker.UI;

/// <summary>XAML: Text="{ui:Tr Key=main.add}"</summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class Tr : MarkupExtension
{
    public string Key { get; set; } = "";
    protected override object ProvideValue() => Strings.T(Key);
}
