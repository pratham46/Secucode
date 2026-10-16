using System.Windows;

namespace Secucode.Desktop.Shell;

// One sidebar entry. Create builds a fresh page each time it is selected.
public sealed record NavItem(string Glyph, string Label, Func<FrameworkElement> Create);
