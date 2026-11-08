using System.Diagnostics;
using System.Reflection;

namespace Ballast.TestKit;

[AttributeUsage(AttributeTargets.Method)]
public sealed class FactAttribute : Attribute
{
    public string? Skip { get; set; }
}

public sealed class AssertException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new AssertException(message ?? "Expected condition to be true");
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition) throw new AssertException(message ?? "Expected condition to be false");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertException(message ?? $"Expected <{expected}> but got <{actual}>");
    }

    public static void Equal(double expected, double actual, double tolerance, string? message = null)
    {
        if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            throw new AssertException(message ?? $"Expected {expected} +/- {tolerance} but got {actual}");
    }

    public static void InRange(double value, double low, double high, string? message = null)
    {
        if (double.IsNaN(value) || value < low || value > high)
            throw new AssertException(message ?? $"Expected {value} within [{low}, {high}]");
    }

    public static void NotNull(object? value, string? message = null)
    {
        if (value is null) throw new AssertException(message ?? "Expected a non-null value");
    }

    public static void Contains(string expectedSubstring, string? actual, string? message = null)
    {
        if (actual is null || !actual.Contains(expectedSubstring, StringComparison.Ordinal))
            throw new AssertException(message ?? $"Expected \"{actual}\" to contain \"{expectedSubstring}\"");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T ex) { return ex; }
        catch (Exception ex) { throw new AssertException($"Expected {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}"); }
        throw new AssertException($"Expected {typeof(T).Name} but nothing was thrown");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T ex) { return ex; }
        catch (Exception ex) { throw new AssertException($"Expected {typeof(T).Name} but got {ex.GetType().Name}: {ex.Message}"); }
        throw new AssertException($"Expected {typeof(T).Name} but nothing was thrown");
    }
}

public static class TestRunner
{
    public static async Task<int> RunAsync(Assembly assembly, string[] args)
    {
        var filter = args.FirstOrDefault(a => !a.StartsWith('-'));
        var cases = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<FactAttribute>() is not null)
                .Select(m => (Type: t, Method: m)))
            .Where(c => filter is null || $"{c.Type.Name}.{c.Method.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Type.FullName).ThenBy(c => c.Method.Name)
            .ToList();

        int passed = 0, failed = 0, skipped = 0;
        var total = Stopwatch.StartNew();
        foreach (var (type, method) in cases)
        {
            var name = $"{type.Name}.{method.Name}";
            var fact = method.GetCustomAttribute<FactAttribute>()!;
            if (fact.Skip is not null)
            {
                skipped++;
                Console.WriteLine($"[SKIP] {name}: {fact.Skip}");
                continue;
            }
            var sw = Stopwatch.StartNew();
            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(type);
                var result = method.Invoke(instance, null);
                if (result is Task task) await task;
                passed++;
                Console.WriteLine($"[PASS] {name} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;
                failed++;
                Console.WriteLine($"[FAIL] {name}: {inner.Message}");
                if (inner is not AssertException) Console.WriteLine(inner.StackTrace);
            }
            finally
            {
                if (instance is IAsyncDisposable ad) await ad.DisposeAsync();
                else if (instance is IDisposable d) d.Dispose();
            }
        }
        Console.WriteLine();
        Console.WriteLine($"Total: {cases.Count}  Passed: {passed}  Failed: {failed}  Skipped: {skipped}  Time: {total.Elapsed.TotalSeconds:F1}s");
        return failed == 0 && cases.Count > 0 ? 0 : 1;
    }
}
