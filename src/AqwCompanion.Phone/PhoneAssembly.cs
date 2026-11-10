using System.Reflection;
using AqwCompanion.Core.Ingestion;

namespace AqwCompanion.Phone;

/// <summary>Marks the assembly for the architecture tests.</summary>
public static class PhoneAssembly
{
    /// <summary>The Core assembly this one builds on; the only one of ours it may reference.</summary>
    public static Assembly CoreAssembly => typeof(PageSnapshot).Assembly;
}
