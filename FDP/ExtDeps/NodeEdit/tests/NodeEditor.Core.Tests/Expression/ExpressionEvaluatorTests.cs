using NodeEditor.Core.Expression;
using FluentAssertions;
using Xunit;

namespace NodeEditor.Core.Tests.Expression;

public class ExpressionEvaluatorTests
{
    [Theory]
    [InlineData("1+1", 2.0)]
    [InlineData("2*pi", Math.PI * 2)]
    [InlineData("1/60", 1.0 / 60)]
    [InlineData("45 deg", Math.PI / 4)]
    [InlineData("sin(pi/2)", 1.0)]
    [InlineData("clamp(5, 0, 1)", 1.0)]
    [InlineData("clamp(-5, 0, 1)", 0.0)]
    [InlineData("1.5e-3", 0.0015)]
    [InlineData("(1+2)*3", 9.0)]
    [InlineData("2^3", 8.0)]
    [InlineData("-5", -5.0)]
    [InlineData("abs(-3.5)", 3.5)]
    [InlineData("min(2,3)", 2.0)]
    [InlineData("max(2,3)", 3.0)]
    public void Eval_Success(string expr, double expected)
    {
        var r = ExpressionEvaluator.Evaluate(expr);
        r.Success.Should().BeTrue($"expr='{expr}' err='{r.Error}'");
        r.Value.Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1+")]
    [InlineData("(1")]
    [InlineData("xyz")]
    [InlineData("clamp(1)")]
    [InlineData("System.IO.File")]
    public void Eval_Failure(string expr)
    {
        var r = ExpressionEvaluator.Evaluate(expr);
        r.Success.Should().BeFalse();
    }
}
