using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using ViShap.Rewind.Benchmarks.Reporting;

namespace ViShap.Rewind.Benchmarks.Runners;

/// <summary>
/// Track A's check before any timing: every benchmark of every suite, under every combination of its parameters, is
/// set up, invoked once and cleaned up, and afterwards no scope may be open on the thread. A benchmark that leaves a
/// scope open would measure every later invocation inside it, which is the harness defect the plan names first.
/// </summary>
internal static class TrackAVerifier
{
    internal sealed record Case(string Suite, string Method, string Parameters, string State, string Detail);

    internal static IReadOnlyList<Case> VerifyAll(IEnumerable<Type> suites)
    {
        var cases = new List<Case>();

        foreach (var suite in suites)
        {
            var methods = suite.GetMethods().Where(method => method.GetCustomAttribute<BenchmarkAttribute>() is not null).ToList();

            foreach (var combination in Combinations(suite))
            {
                var parameters = string.Join("; ", combination.Select(pair => $"{pair.Property.Name}={Text(pair.Value)}"));

                foreach (var method in methods)
                {
                    var (state, detail) = OnThread(() => Invoke(suite, combination, method));
                    cases.Add(new Case(suite.Name, method.Name, parameters, state, detail));
                }
            }
        }

        return cases;
    }

    internal static void Write(IReadOnlyList<Case> cases, string path) =>
        Csv.Write(path, ["suite", "method", "parameters", "state", "detail"],
            cases.Select(@case => (IReadOnlyList<string>)[@case.Suite, @case.Method, @case.Parameters, @case.State, @case.Detail]));

    private static (string State, string Detail) Invoke(Type suite, IReadOnlyList<(PropertyInfo Property, object? Value)> combination, MethodInfo method)
    {
        var instance = Activator.CreateInstance(suite)!;

        foreach (var (property, value) in combination)
        {
            property.SetValue(instance, value);
        }

        Call(instance, typeof(GlobalSetupAttribute));

        try
        {
            var result = method.Invoke(instance, null);

            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }

            if (Rewind.Current is not null)
            {
                return ("Failed", "a scope is still open after one invocation");
            }
        }
        finally
        {
            Call(instance, typeof(GlobalCleanupAttribute));
        }

        return (ResultRow.Supported, string.Empty);
    }

    private static void Call(object instance, Type attribute)
    {
        foreach (var method in instance.GetType().GetMethods().Where(method => method.IsDefined(attribute, inherit: true)))
        {
            method.Invoke(instance, null);
        }
    }

    /// <summary>Every combination of the suite's parameters, read from its <c>[Params]</c> and <c>[ParamsSource]</c>.</summary>
    private static IEnumerable<IReadOnlyList<(PropertyInfo Property, object? Value)>> Combinations(Type suite)
    {
        IEnumerable<IReadOnlyList<(PropertyInfo, object?)>> result = [[]];

        foreach (var property in suite.GetProperties())
        {
            object?[]? values = null;

            if (property.GetCustomAttribute<ParamsAttribute>() is { } fixedValues)
            {
                values = fixedValues.Values;
            }
            else if (property.GetCustomAttribute<ParamsSourceAttribute>() is { } source)
            {
                var member = suite.GetMember(source.Name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).First();
                var target = member is PropertyInfo { GetMethod.IsStatic: true } or MethodInfo { IsStatic: true } ? null : Activator.CreateInstance(suite);
                var enumerable = member switch
                {
                    PropertyInfo info => info.GetValue(target),
                    MethodInfo info => info.Invoke(target, null),
                    _ => null,
                };

                values = [.. ((System.Collections.IEnumerable)enumerable!).Cast<object?>()];
            }

            if (values is null)
            {
                continue;
            }

            var captured = values;
            result = [.. result.SelectMany(prefix => captured.Select(value => (IReadOnlyList<(PropertyInfo, object?)>)[.. prefix, (property, value)]))];
        }

        return result;
    }

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";

    private static (string State, string Detail) OnThread(Func<(string State, string Detail)> check)
    {
        (string State, string Detail) outcome = ("Failed", "the check did not run");

        var thread = new Thread(() =>
        {
            try
            {
                outcome = check();
            }
            catch (Exception exception)
            {
                var inner = exception is TargetInvocationException { InnerException: { } cause } ? cause : exception;
                outcome = ("Failed", $"{inner.GetType().Name}: {inner.Message}");
            }
        })
        {
            IsBackground = true,
            Name = "verify Track A",
        };

        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(60)) ? outcome : ("Failed", "the check did not finish in 60 seconds");
    }
}
