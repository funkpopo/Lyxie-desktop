using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Layout;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Lyxie_desktop.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lyxie_desktop.Helpers;

/// <summary>
/// Markdown 渲染器，将 Markdown 文档转换为 Avalonia 控件
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// 将 Markdown 文本渲染为 Avalonia 控件集合
    /// </summary>
    public static StackPanel RenderToPanel(string markdownText)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8
        };

        if (string.IsNullOrWhiteSpace(markdownText))
            return panel;

        try
        {
            var document = Markdig.Markdown.Parse(markdownText, Pipeline);
            
            foreach (var block in document)
            {
                var control = RenderBlock(block);
                if (control != null)
                {
                    panel.Children.Add(control);
                }
            }
        }
        catch (Exception ex)
        {
            // 如果解析失败，显示原始文本
            var errorText = new SelectableTextBlock
            {
                Text = markdownText,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse("#FF6B6B")
            };
            panel.Children.Add(errorText);
            System.Diagnostics.Debug.WriteLine($"Markdown 解析错误: {ex.Message}");
        }

        return panel;
    }

    private static Control? RenderBlock(Block block)
    {
        return block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock paragraph => RenderParagraph(paragraph),
            ListBlock list => RenderList(list),
            CodeBlock code => RenderCodeBlock(code),
            QuoteBlock quote => RenderQuote(quote),
            ThematicBreakBlock => RenderHorizontalRule(),
            Table table => RenderTable(table),
            _ => RenderParagraph(block as ParagraphBlock ?? new ParagraphBlock())
        };
    }

    private static SelectableTextBlock RenderHeading(HeadingBlock heading)
    {
        var textBlock = new SelectableTextBlock
        {
            FontSize = heading.Level switch
            {
                1 => 24,
                2 => 20,
                3 => 18,
                4 => 16,
                5 => 14,
                _ => 12
            },
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, heading.Level == 1 ? 16 : 12, 0, 8)
        };

        if (heading.Inline != null)
        {
            RenderInlineToTextBlock(textBlock, heading.Inline);
        }

        return textBlock;
    }

    private static Control RenderParagraph(ParagraphBlock paragraph)
    {
        if (paragraph?.Inline == null)
        {
            return new SelectableTextBlock { Text = "", TextWrapping = TextWrapping.Wrap };
        }

        // 处理混合内容（文本和图片）
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var currentText = new StringBuilder();
        var hasContent = false;

        foreach (var inline in paragraph.Inline)
        {
            if (inline is LinkInline link && link.IsImage)
            {
                // 如果之前有文本，先添加文本块
                if (currentText.Length > 0)
                {
                    var textBlock = new SelectableTextBlock
                    {
                        Text = currentText.ToString(),
                        TextWrapping = TextWrapping.Wrap
                    };
                    panel.Children.Add(textBlock);
                    currentText.Clear();
                }

                // 添加图片
                panel.Children.Add(RenderImage(link));
                hasContent = true;
            }
            else if (inline is LiteralInline literal)
            {
                currentText.Append(literal.Content);
                hasContent = true;
            }
            else if (inline is EmphasisInline emphasis)
            {
                var style = emphasis.DelimiterCount == 2 ? "bold" : "italic";
                if (emphasis.DelimiterCount == 3)
                {
                    style = "bold-italic";
                }
                
                foreach (var child in emphasis)
                {
                    if (child is LiteralInline lit)
                    {
                        currentText.Append(lit.Content);
                    }
                }
                hasContent = true;
            }
            else if (inline is CodeInline code)
            {
                currentText.Append(code.Content);
                hasContent = true;
            }
        }

        // 添加剩余的文本
        if (currentText.Length > 0)
        {
            var textBlock = new SelectableTextBlock
            {
                Text = currentText.ToString(),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(textBlock);
        }

        return hasContent ? panel : new SelectableTextBlock { Text = "", TextWrapping = TextWrapping.Wrap };
    }

    private static void RenderInlineToTextBlock(SelectableTextBlock textBlock, ContainerInline container)
    {
        var inlineText = new List<string>();
        var currentStyle = new Stack<(string style, int start)>();
        var currentText = new StringBuilder();

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    currentText.Append(literal.Content);
                    break;

                case EmphasisInline emphasis:
                    var style = emphasis.DelimiterCount == 2 ? "bold" : "italic";
                    if (emphasis.DelimiterCount == 3)
                    {
                        style = "bold-italic";
                    }
                    currentStyle.Push((style, currentText.Length));
                    foreach (var child in emphasis)
                    {
                        if (child is LiteralInline lit)
                        {
                            currentText.Append(lit.Content);
                        }
                    }
                    ApplyStyle(currentText, currentStyle.Pop(), style);
                    break;

                case CodeInline code:
                    var codeStart = currentText.Length;
                    currentText.Append(code.Content);
                    ApplyStyle(currentText, ("code", codeStart), "code");
                    break;

                case LinkInline link:
                    if (!link.IsImage)
                    {
                        var linkStart = currentText.Length;
                        currentText.Append(link.Title ?? link.Url);
                        ApplyStyle(currentText, ("link", linkStart), "link");
                    }
                    break;

                case AutolinkInline autolink:
                    var autolinkStart = currentText.Length;
                    currentText.Append(autolink.Url);
                    ApplyStyle(currentText, ("link", autolinkStart), "link");
                    break;
            }
        }

        textBlock.Text = currentText.ToString();
    }

    private static void ApplyStyle(StringBuilder text, (string style, int start) styleInfo, string type)
    {
        var length = text.Length - styleInfo.start;
        var content = text.ToString(styleInfo.start, length);

        switch (type)
        {
            case "bold":
                text.Remove(styleInfo.start, length).Insert(styleInfo.start, $"**{content}**");
                break;
            case "italic":
                text.Remove(styleInfo.start, length).Insert(styleInfo.start, $"*{content}*");
                break;
            case "bold-italic":
                text.Remove(styleInfo.start, length).Insert(styleInfo.start, $"***{content}***");
                break;
            case "code":
                text.Remove(styleInfo.start, length).Insert(styleInfo.start, $"`{content}`");
                break;
            case "link":
                text.Remove(styleInfo.start, length).Insert(styleInfo.start, $"[{content}]");
                break;
        }
    }

    private static Control RenderList(ListBlock list)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };

        int index = 1;
        foreach (var item in list.Cast<ListItemBlock>())
        {
            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Avalonia.Thickness(20, 0, 0, 0)
            };

            // 列表标记
            var marker = new SelectableTextBlock
            {
                Text = list.IsOrdered ? $"{index}." : "•",
                VerticalAlignment = VerticalAlignment.Top,
                MinWidth = 20
            };
            itemPanel.Children.Add(marker);

            // 列表内容
            var contentPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4
            };

            foreach (var block in item)
            {
                var control = RenderBlock(block);
                if (control != null)
                {
                    contentPanel.Children.Add(control);
                }
            }

            itemPanel.Children.Add(contentPanel);
            panel.Children.Add(itemPanel);
            index++;
        }

        return panel;
    }

    private static Control RenderCodeBlock(CodeBlock codeBlock)
    {
        var text = codeBlock is FencedCodeBlock fenced 
            ? fenced.Lines.ToString() 
            : codeBlock.Lines.ToString();

        var language = codeBlock is FencedCodeBlock fencedBlock 
            ? fencedBlock.Info ?? string.Empty 
            : string.Empty;

        // 使用新的代码块控件
        var codeBlockControl = new CodeBlockControl();
        codeBlockControl.SetCodeContent(text, language);
        
        return codeBlockControl;
    }

    private static Control RenderQuote(QuoteBlock quote)
    {
        var border = new Border
        {
            BorderBrush = Brush.Parse("#DFE2E5"),
            BorderThickness = new Avalonia.Thickness(4, 0, 0, 0),
            Background = Brush.Parse("#F6F8FA"),
            Padding = new Avalonia.Thickness(16, 12, 16, 12),
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4
        };

        foreach (var block in quote)
        {
            var control = RenderBlock(block);
            if (control != null)
            {
                // 为引用内容设置特殊样式
                if (control is SelectableTextBlock tb)
                {
                    tb.Foreground = Brush.Parse("#6A737D");
                    tb.FontStyle = FontStyle.Italic;
                }
                panel.Children.Add(control);
            }
        }

        border.Child = panel;
        return border;
    }

    private static Control RenderHorizontalRule()
    {
        return new Border
        {
            Height = 1,
            Background = Brush.Parse("#E1E4E8"),
            Margin = new Avalonia.Thickness(0, 16, 0, 16)
        };
    }

    private static Control RenderTable(Table table)
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        // 获取表格行
        var rows = table.Descendants<TableRow>().ToList();
        if (rows.Count == 0)
            return grid;

        // 添加列定义
        var firstRow = rows[0];
        var cells = firstRow.Descendants<TableCell>().ToList();
        for (int i = 0; i < cells.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        // 添加行定义
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        // 渲染所有行
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var isHeader = rowIndex == 0; // 第一行作为表头
            var rowCells = row.Descendants<TableCell>().ToList();

            for (int colIndex = 0; colIndex < rowCells.Count; colIndex++)
            {
                var cell = rowCells[colIndex];
                var border = new Border
                {
                    BorderBrush = Brush.Parse("#E1E4E8"),
                    BorderThickness = new Thickness(1),
                    Background = isHeader ? Brush.Parse("#F6F8FA") : null,
                    Padding = new Thickness(8)
                };

                var cellText = string.Empty;
                if (cell.Count > 0 && cell[0] is ParagraphBlock paragraph && paragraph.Inline != null)
                {
                    cellText = ExtractInlineText(paragraph.Inline);
                }

                var text = new SelectableTextBlock
                {
                    Text = cellText,
                    FontWeight = isHeader ? FontWeight.Bold : FontWeight.Normal,
                    TextWrapping = TextWrapping.Wrap
                };

                border.Child = text;
                Grid.SetColumn(border, colIndex);
                Grid.SetRow(border, rowIndex);
                grid.Children.Add(border);
            }
        }

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid
        };

        return scrollViewer;
    }

    private static Control RenderImage(LinkInline imageLink)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Margin = new Thickness(0, 8)
        };

        try
        {
            var img = new Image
            {
                Source = new Bitmap(imageLink.Url),
                Stretch = Stretch.Uniform,
                MaxHeight = 400,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            panel.Children.Add(img);

            // 添加图片标题（如果有）
            if (!string.IsNullOrEmpty(imageLink.Title))
            {
                var caption = new TextBlock
                {
                    Text = imageLink.Title,
                    FontStyle = FontStyle.Italic,
                    Foreground = Brush.Parse("#666666"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                panel.Children.Add(caption);
            }
        }
        catch (Exception ex)
        {
            // 图片加载失败时显示错误信息
            var errorText = new TextBlock
            {
                Text = $"无法加载图片：{imageLink.Url}",
                Foreground = Brush.Parse("#FF6B6B"),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(errorText);
            System.Diagnostics.Debug.WriteLine($"图片加载失败: {ex.Message}");
        }

        return panel;
    }

    private static string ExtractInlineText(Markdig.Syntax.Inlines.Inline? inline)
    {
        if (inline == null) return string.Empty;

        return inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => code.Content,
            EmphasisInline emphasis => ExtractInlineText(emphasis.FirstChild),
            LinkInline link => ExtractInlineText(link.FirstChild),
            ContainerInline container => ExtractInlineText(container),
            _ => string.Empty
        };
    }

    private static string ExtractInlineText(ContainerInline? container)
    {
        if (container == null) return string.Empty;

        var text = string.Empty;
        foreach (var inline in container)
        {
            text += ExtractInlineText(inline);
        }
        return text;
    }
} 