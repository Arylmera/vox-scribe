using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>The edit pass: an instruction applied to a selection, with only a mechanical guard.</summary>
public sealed class TextEditorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("<think>hmm</think>Hello")]
    public void An_unusable_answer_vets_as_null(string? answer) => TextEditor.Vet(answer).ShouldBeNull();

    [Fact]
    public void A_fence_around_the_answer_is_stripped() =>
        TextEditor.Vet("```\nHello world\n```").ShouldBe("Hello world");

    /// <summary>No overlap guard: a translation shares no words with what was selected.</summary>
    [Fact]
    public void A_translation_is_kept() =>
        TextEditor.Vet("The build passes").ShouldBe("The build passes");

    [Fact]
    public void The_request_carries_instruction_and_text()
    {
        var request = TextEditor.Request("le build passe", "traduis en anglais");

        request.ShouldContain("traduis en anglais");
        request.ShouldContain("<<<\nle build passe\n>>>");
    }

    [Fact]
    public async Task An_unreachable_gateway_fails_with_a_reason()
    {
        var editor = new TextEditor("http://127.0.0.1:9/v1", "local-light", null);

        var outcome = await editor.EditAsync("le build passe", "traduis en anglais", CancellationToken.None);

        outcome.Text.ShouldBeNull();
        outcome.Failure.ShouldNotBeNullOrWhiteSpace();
    }
}
