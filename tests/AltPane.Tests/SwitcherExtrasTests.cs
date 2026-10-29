using AltPane.Core.Input;
using AltPane.Core.Models;
using AltPane.Core.Search;
using Xunit;

namespace AltPane.Tests;

public class ExpressionTests
{
    [Theory]
    [InlineData("1+2*3", "7")]
    [InlineData("(1+2)*3", "9")]
    [InlineData("2^3", "8")]
    [InlineData("sqrt(9)", "3")]
    [InlineData("abs(-3)", "3")]
    [InlineData("round(2.5)", "3")]
    [InlineData("sin(0)", "0")]
    [InlineData("cos(0)", "1")]
    [InlineData("log(100)", "2")]
    [InlineData("ln(1)", "0")]
    [InlineData("10%3", "1")]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("=1+1", "2")]
    [InlineData("pi", "3.1415926536")]
    [InlineData("1.5+2.5", "4")]
    [InlineData("1000+1", "1001")]
    public void Evaluates(string text, string plain)
    {
        Assert.True(Expression.TryEvaluate(text, out var value));
        Assert.Equal(plain, value.Plain);
        Assert.Equal("= " + plain, value.Shown);
    }

    [Fact]
    public void Full_width_operators_match_the_ascii_expression()
    {
        Assert.True(Expression.TryEvaluate("（1＋2）×3－4÷2", out var value));
        Assert.Equal("7", value.Plain);
    }

    [Theory]
    [InlineData("")]
    [InlineData("=")]
    [InlineData("1+")]
    [InlineData("1/0")]
    [InlineData("sqrt(-1)")]
    [InlineData("log(0)")]
    [InlineData("hello")]
    [InlineData("2++")]
    public void Invalid_expressions_are_not_results(string text) =>
        Assert.False(Expression.TryEvaluate(text, out _));
}

public class FileSearchTests
{
    [Fact]
    public void Documents_are_filtered_and_sorted_by_run_count()
    {
        Assert.True(FileSearch.TryPlan("report", FileSearch.DefaultList, null, out var plan));
        Assert.False(plan.AllTypes);
        Assert.Contains("report", plan.Query, StringComparison.Ordinal);
        Assert.Contains("ext:doc;docx;", plan.Query, StringComparison.Ordinal);
        Assert.Contains(";pdf;", plan.Query, StringComparison.Ordinal);
        Assert.Contains("ofd", plan.Query, StringComparison.Ordinal);
        Assert.Contains("epub", plan.Query, StringComparison.Ordinal);
        Assert.EndsWith(FileSearch.Sort, plan.Query);
        Assert.DoesNotContain("ext:exe", plan.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("f report")]
    [InlineData("F  report")]
    [InlineData("file:report")]
    [InlineData("FILE:report")]
    public void A_prefix_searches_every_type(string text)
    {
        Assert.True(FileSearch.TryPlan(text, FileSearch.DefaultList, null, out var plan));
        Assert.True(plan.AllTypes);
        Assert.Equal("report", plan.Text);
        Assert.DoesNotContain("ext:", plan.Query, StringComparison.Ordinal);
        Assert.Contains(FileSearch.Sort, plan.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("f ")]
    [InlineData("file:")]
    [InlineData("file:   ")]
    public void An_empty_file_query_is_not_sent(string text) =>
        Assert.False(FileSearch.TryPlan(text, FileSearch.DefaultList, null, out _));

    [Fact]
    public void Firefox_is_a_document_query_not_the_all_files_prefix()
    {
        Assert.True(FileSearch.TryPlan("firefox", null, null, out var plan));
        Assert.False(plan.AllTypes);
        Assert.Contains("ext:", plan.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_extension_setting_uses_the_document_list()
    {
        var list = FileSearch.Split("  ");
        Assert.Contains("pdf", list);
        Assert.Contains("wps", list);
        Assert.Equal(FileSearch.DefaultDocuments.Length, list.Count);
    }
}

public class DigitSelectTests
{
    [Theory]
    [InlineData(0x31, 0)]
    [InlineData(0x39, 8)]
    [InlineData(0x30, 9)]
    [InlineData(0x61, 0)]
    [InlineData(0x60, 9)]
    public void Ctrl_number_maps_to_a_visible_row(int key, int index)
    {
        Assert.Equal(index, DigitSelect.Index(key));
        Assert.Equal(index == 9 ? "0" : (index + 1).ToString(), DigitSelect.Label(index));
    }

    [Fact]
    public void The_hook_selects_that_row_while_alt_is_held_and_while_searching()
    {
        var alt = new HookInput
        {
            SwitcherOpen = true,
            GestureLatched = true,
            LatchedTrigger = ModifierKeys.Alt,
            Hotkey = HotkeyChord.AltTab(),
            VirtualKey = 0x31,
            IsKeyDown = true,
            Modifiers = new ModifierState(true, true, false, false)
        };
        var held = HookInterpreter.Interpret(alt);
        Assert.Equal(HookCommand.ActivateRow, held.Command);
        Assert.Equal(0, held.Delta);

        var search = alt with
        {
            SearchMode = true,
            GestureLatched = false,
            Modifiers = new ModifierState(false, true, false, false),
            VirtualKey = 0x30
        };
        var typed = HookInterpreter.Interpret(search);
        Assert.Equal(HookCommand.ActivateRow, typed.Command);
        Assert.Equal(9, typed.Delta);

        var plain = search with { Modifiers = ModifierState.None, VirtualKey = 0x31 };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(plain).Command);
    }

    [Fact]
    public void Labels_stop_after_the_tenth_row()
    {
        Assert.Equal("1", DigitSelect.Label(0));
        Assert.Equal("0", DigitSelect.Label(9));
        Assert.Equal("", DigitSelect.Label(10));
    }
}

public class RowHintTests
{
    [Fact]
    public void Each_row_type_has_its_own_hint()
    {
        Assert.Equal(RowHints.File, RowHints.Text(HintRow.File, show: true));
        Assert.Equal(RowHints.Window, RowHints.Text(HintRow.Window, show: true));
        Assert.Equal(RowHints.WindowNoPath, RowHints.Text(HintRow.Window, show: true, hasPath: false));
        Assert.Equal(RowHints.App, RowHints.Text(HintRow.App, show: true));
        Assert.Equal(RowHints.AppSwitch, RowHints.Text(HintRow.App, show: true, switching: true));
        Assert.Equal(RowHints.AppNoPath, RowHints.Text(HintRow.App, show: true, hasPath: false));
        Assert.Equal(RowHints.Calculator, RowHints.Text(HintRow.Calculator, show: true));
        Assert.Equal("", RowHints.Text(HintRow.File, show: false));
        Assert.Equal("", RowHints.Text(null, show: true));
    }

    [Fact]
    public void The_file_hint_names_the_shortcuts()
    {
        Assert.Contains("Enter 打开", RowHints.File, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Enter 打开所在目录", RowHints.File, StringComparison.Ordinal);
        Assert.Contains("Ctrl+C 复制路径", RowHints.File, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Shift+C 复制目录路径", RowHints.File, StringComparison.Ordinal);
        Assert.Contains("→ 更多", RowHints.File, StringComparison.Ordinal);
        Assert.Equal("Enter/Ctrl+C 复制结果", RowHints.Calculator);
    }
}
