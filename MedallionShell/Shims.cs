using System;
using System.Collections.Generic;
using System.Text;

namespace System.Diagnostics.CodeAnalysis
{
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1649:File name should match first type name", Justification = "Shim")]
    [System.AttributeUsage(System.AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) { this.ReturnValue = returnValue; }

        public bool ReturnValue { get; }
    }
}
