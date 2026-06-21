using Prokudin.Core.Retouch;

namespace Prokudin.Gui.ViewModels;

public sealed record AutoCleanMaskEditOperation(
    RetouchPoint Start,
    RetouchPoint End,
    AutoCleanMaskEditAction Action,
    int BrushSize,
    bool IsRectangle);
