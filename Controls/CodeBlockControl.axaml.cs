using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lyxie_desktop.Controls;

public partial class CodeBlockControl : UserControl
{
    private string _codeContent = string.Empty;
    private string _language = string.Empty;
    private readonly DispatcherTimer _resetCopyButtonTimer;

    public CodeBlockControl()
    {
        InitializeComponent();

        var copyButton = this.FindControl<Button>("CopyButton");
        if (copyButton != null)
        {
            copyButton.Click += CopyButton_Click;
        }

        _resetCopyButtonTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _resetCopyButtonTimer.Tick += ResetCopyButton;
    }

    public void SetCodeContent(string code, string language = "")
    {
        _codeContent = code?.Trim() ?? string.Empty;
        _language = language?.Trim().ToLower() ?? string.Empty;

        var codeText = this.FindControl<SelectableTextBlock>("CodeText");
        var languageText = this.FindControl<TextBlock>("LanguageText");
        var lineNumbers = this.FindControl<ItemsControl>("LineNumbers");

        if (codeText != null)
        {
            codeText.Text = _codeContent;
        }

        if (languageText != null)
        {
            languageText.Text = string.IsNullOrEmpty(_language) ? "plain text" : _language;
        }

        if (lineNumbers != null)
        {
            var lines = _codeContent.Split('\n');
            var lineNumbersList = new List<string>();
            for (int i = 1; i <= lines.Length; i++)
            {
                lineNumbersList.Add(i.ToString("D3"));
            }
            lineNumbers.ItemsSource = lineNumbersList;
        }

        ApplySyntaxHighlighting();
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(_codeContent);
                
                var copyText = this.FindControl<TextBlock>("CopyText");
                var copyIcon = this.FindControl<PathIcon>("CopyIcon");
                
                if (copyText != null)
                {
                    copyText.Text = "已复制";
                    copyText.Foreground = Brush.Parse("#4CAF50");
                }

                if (copyIcon != null)
                {
                    copyIcon.Data = Geometry.Parse("M9,16.17L4.83,12l-1.42,1.41L9,19 21,7l-1.41-1.41L9,16.17z");
                    copyIcon.Foreground = Brush.Parse("#4CAF50");
                }

                _resetCopyButtonTimer.Start();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"复制代码失败: {ex.Message}");
        }
    }

    private void ResetCopyButton(object? sender, EventArgs e)
    {
        _resetCopyButtonTimer.Stop();

        var copyText = this.FindControl<TextBlock>("CopyText");
        var copyIcon = this.FindControl<PathIcon>("CopyIcon");

        if (copyText != null)
        {
            copyText.Text = "复制";
            copyText.Foreground = Brush.Parse("#808080");
        }

        if (copyIcon != null)
        {
            copyIcon.Data = Geometry.Parse("M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z");
            copyIcon.Foreground = Brush.Parse("#808080");
        }
    }

    private void ApplySyntaxHighlighting()
    {
        if (string.IsNullOrEmpty(_codeContent) || string.IsNullOrEmpty(_language))
            return;

        var codeText = this.FindControl<SelectableTextBlock>("CodeText");
        if (codeText == null)
            return;

        // 这里可以根据不同的语言添加语法高亮规则
        switch (_language.ToLower())
        {
            case "csharp":
            case "cs":
                ApplyCSharpHighlighting(codeText);
                break;
            case "javascript":
            case "js":
                ApplyJavaScriptHighlighting(codeText);
                break;
            case "python":
            case "py":
                ApplyPythonHighlighting(codeText);
                break;
            // 可以添加更多语言的支持
        }
    }

    private void ApplyCSharpHighlighting(SelectableTextBlock codeText)
    {
        var keywords = new[] { "using", "namespace", "class", "public", "private", "protected", "internal", "static", "void", "int", "string", "bool", "var", "new", "return", "if", "else", "while", "for", "foreach", "try", "catch", "finally", "throw", "async", "await" };
        var keywordPattern = $@"\b({string.Join("|", keywords)})\b";
        var stringPattern = @"""[^""\\]*(?:\\.[^""\\]*)*""";
        var commentPattern = @"//.*?$";
        var numberPattern = @"\b\d+\b";

        var text = codeText.Text;
        
        // 应用语法高亮
        ApplyHighlighting(text, keywordPattern, "#569CD6"); // 关键字
        ApplyHighlighting(text, stringPattern, "#CE9178"); // 字符串
        ApplyHighlighting(text, commentPattern, "#6A9955", RegexOptions.Multiline); // 注释
        ApplyHighlighting(text, numberPattern, "#B5CEA8"); // 数字
    }

    private void ApplyJavaScriptHighlighting(SelectableTextBlock codeText)
    {
        var keywords = new[] { "const", "let", "var", "function", "return", "if", "else", "for", "while", "do", "break", "continue", "switch", "case", "default", "try", "catch", "finally", "throw", "async", "await", "class", "extends", "new", "this", "super" };
        var keywordPattern = $@"\b({string.Join("|", keywords)})\b";
        var stringPattern = @"(?:""[^""\\]*(?:\\.[^""\\]*)*""|'[^'\\]*(?:\\.[^'\\]*)*')";
        var commentPattern = @"//.*?$|/\*[\s\S]*?\*/";
        var numberPattern = @"\b\d+\.?\d*\b";

        var text = codeText.Text;
        
        // 应用语法高亮
        ApplyHighlighting(text, keywordPattern, "#C586C0"); // 关键字
        ApplyHighlighting(text, stringPattern, "#CE9178"); // 字符串
        ApplyHighlighting(text, commentPattern, "#6A9955", RegexOptions.Multiline); // 注释
        ApplyHighlighting(text, numberPattern, "#B5CEA8"); // 数字
    }

    private void ApplyPythonHighlighting(SelectableTextBlock codeText)
    {
        var keywords = new[] { "def", "class", "if", "else", "elif", "while", "for", "in", "try", "except", "finally", "with", "as", "import", "from", "return", "yield", "break", "continue", "pass", "raise", "True", "False", "None" };
        var keywordPattern = $@"\b({string.Join("|", keywords)})\b";
        var stringPattern = @"(?:""[^""\\]*(?:\\.[^""\\]*)*""|'[^'\\]*(?:\\.[^'\\]*)*')";
        var commentPattern = @"#.*?$";
        var numberPattern = @"\b\d+\.?\d*\b";

        var text = codeText.Text;
        
        // 应用语法高亮
        ApplyHighlighting(text, keywordPattern, "#569CD6"); // 关键字
        ApplyHighlighting(text, stringPattern, "#CE9178"); // 字符串
        ApplyHighlighting(text, commentPattern, "#6A9955", RegexOptions.Multiline); // 注释
        ApplyHighlighting(text, numberPattern, "#B5CEA8"); // 数字
    }

    private void ApplyHighlighting(string text, string pattern, string color, RegexOptions options = RegexOptions.None)
    {
        var codeText = this.FindControl<SelectableTextBlock>("CodeText");
        if (codeText == null)
            return;

        var matches = Regex.Matches(text, pattern, options);
        foreach (Match match in matches)
        {
            var start = match.Index;
            var length = match.Length;
            
            // 这里需要实现实际的语法高亮逻辑
            // 由于 Avalonia 的 TextBlock 不支持直接的文本格式化
            // 我们可以考虑使用 RichTextBlock 或其他替代方案
            // 目前先用颜色标记作为占位
            System.Diagnostics.Debug.WriteLine($"Highlighting: {match.Value} with color {color}");
        }
    }
} 