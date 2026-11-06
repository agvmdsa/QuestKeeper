using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AqwCompanion.Tests.TestDoubles;

/// <summary>Finds controls by <c>x:Name</c> through the visual tree; every UserControl has its own name scope, so FindControl on an outer view cannot see inside child views.</summary>
public static class VisualTreeFinder
{
    public static T Named<T>(this Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"Control {name} not found in the visual tree");
}
