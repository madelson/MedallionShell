using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Medallion.Shell;
using NUnit.Framework;

namespace MedallionShell.Tests.Streams;

internal class StreamImplementationTest
{
#if NETCOREAPP
    [TestCase(typeof(Stream))]
    [TestCase(typeof(TextWriter))]
    [TestCase(typeof(TextReader))]
    public void TestAllStreamImplementationsOverrideSpanMethods(Type baseType)
    {
        var requiredMethods = baseType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(
                m => m.IsVirtual
                    && (m.IsPublic || m.Attributes.HasFlag(MethodAttributes.FamORAssem) || m.Attributes.HasFlag(MethodAttributes.Family))
                    && m.GetParameters().Select(p => p.ParameterType)
                        .Any(
                            t => t.IsConstructedGenericType
                                && new[] { typeof(Span<>), typeof(ReadOnlySpan<>), typeof(Memory<>), typeof(ReadOnlyMemory<>) }.Contains(t.GetGenericTypeDefinition())
                        )
            )
            .ToArray();

        var implementingTypes = typeof(Command).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(baseType));

        List<string> violations = new();
        foreach (var implementation in implementingTypes)
        {
            var implementationMethods = implementation.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in requiredMethods)
            {
                var implementingMethod = implementationMethods.Single(
                    m => m.Name == method.Name
                        && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(method.GetParameters().Select(p => p.ParameterType))
                );
                if (implementingMethod.DeclaringType!.Assembly != typeof(Command).Assembly)
                {
                    violations.Add($"{implementation} must implement {method}");
                }
            }
        }

        Assert.IsEmpty(violations);
    }
#endif
}
