using Torch;
using Torch.Views;

namespace Essentials;

public class InfoCommand : ViewModel
{
    [Display(Order = 1, Name = "Command", Description = "Type this in chat to activate command")]
    public string? Command { get; set => SetValue(ref field, value); }

    [Display(Order = 2, Name = "Chat Response", Description = "Chat response to command")]
    public string? ChatResponse { get; set => SetValue(ref field, value); }

    [Display(Order = 3, Name = "Dialog Response", Description = "Dialog box response")]
    public string? DialogResponse { get; set => SetValue(ref field, value); }

    [Display(Order = 4, Name = "URL", Description = "url response to command")]
    public string? URL { get; set => SetValue(ref field, value); }

    public override string? ToString() { return Command; } }