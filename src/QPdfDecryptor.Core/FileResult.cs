namespace QPdfDecryptor.Core;

public sealed record FileResult(
    string InputPath,
    string? OutputPath,
    FileOutcome Outcome,
    string? MatchedPassword,
    string Message,
    string Details)
{
    public override string ToString() =>
        $"{nameof(FileResult)} {{ {nameof(InputPath)} = {InputPath}, {nameof(OutputPath)} = {OutputPath ?? "<null>"}, {nameof(Outcome)} = {Outcome}, {nameof(MatchedPassword)} = {(MatchedPassword is null ? "<null>" : "***")}, {nameof(Message)} = {Message}, {nameof(Details)} = {Details} }}";
}
