using FluentAssertions;
using StructEdit.Core;

namespace StructEdit.Tests.Foundation;

public class ValidationResultTests
{
    [Fact]
    public void Ok_IsValid_ReturnsTrue()
    {
        var result = ValidationResult.Ok();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Ok_Errors_IsEmpty()
    {
        var result = ValidationResult.Ok();
        result.Errors.Count.Should().Be(0);
    }

    [Fact]
    public void Fail_WithErrors_IsInvalid()
    {
        var error = new ValidationError("$.X", "Value out of range");
        var result = ValidationResult.Fail(new[] { error });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Fail_PreservesErrors_MessageMatches()
    {
        var error = new ValidationError("$.Speed", "Too fast");
        var result = ValidationResult.Fail(new[] { error });
        result.Errors[0].Message.Should().Be("Too fast");
    }

    [Fact]
    public void Fail_PreservesErrors_JsonPathMatches()
    {
        var error = new ValidationError("$.Speed", "Too fast");
        var result = ValidationResult.Fail(new[] { error });
        result.Errors[0].JsonPath.Should().Be("$.Speed");
    }

    // DEBT-02: Fail(empty) must coerce to Ok
    [Fact]
    public void Fail_EmptyList_ReturnsOk()
    {
        var result = ValidationResult.Fail(Enumerable.Empty<ValidationError>());
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // DEBT-02: Fail(single error) still returns invalid
    [Fact]
    public void Fail_SingleError_ReturnsInvalid()
    {
        var result = ValidationResult.Fail(new[] { new ValidationError("$.X", "fail") });
        result.IsValid.Should().BeFalse();
    }

    // Extra: Fail(empty list via ToList) also returns Ok
    [Fact]
    public void Fail_EmptyListViaToList_ReturnsOk()
    {
        var emptyList = new List<ValidationError>();
        var result = ValidationResult.Fail(emptyList);
        result.IsValid.Should().BeTrue();
    }

    // Extra: Ok result errors list is read-only and empty
    [Fact]
    public void Ok_ErrorsList_IsReadOnly()
    {
        var result = ValidationResult.Ok();
        result.Errors.Should().BeEmpty();
        result.Errors.Should().BeAssignableTo<IReadOnlyList<ValidationError>>();
    }
}

public class EditValidationExceptionTests
{
    [Fact]
    public void EditValidationException_CarriesResult_IsInvalid()
    {
        var failResult = ValidationResult.Fail(new[]
        {
            new ValidationError("$.HP", "Below zero"),
        });
        var ex = new EditValidationException(failResult);
        ex.Result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void EditValidationException_IsException()
    {
        var result = ValidationResult.Fail(new[] { new ValidationError("$.X", "err") });
        var ex = new EditValidationException(result);
        ex.Should().BeAssignableTo<Exception>();
    }
}

public class EditRebuildStateTests
{
    [Fact]
    public void EditRebuildState_HasThreeValues()
    {
        var values = Enum.GetValues<EditRebuildState>();
        values.Length.Should().Be(3);
    }

    [Fact]
    public void EditRebuildState_ContainsExpectedValues()
    {
        Enum.GetValues<EditRebuildState>().Should().Contain(EditRebuildState.Stable);
        Enum.GetValues<EditRebuildState>().Should().Contain(EditRebuildState.RebuildSuggested);
        Enum.GetValues<EditRebuildState>().Should().Contain(EditRebuildState.RebuildRequired);
    }
}
