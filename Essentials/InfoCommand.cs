using Torch;

namespace Essentials;

public class InfoCommand : ViewModel
{
    public string? Command { get; set => SetValue(ref field, value); }

    public string? ChatResponse { get; set => SetValue(ref field, value); }

    public string? DialogResponse { get; set => SetValue(ref field, value); }

    public string? URL { get; set => SetValue(ref field, value); }

    public override string? ToString() { return Command; }
}
