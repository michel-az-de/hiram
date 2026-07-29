using System.Reflection;
using Hiram.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hiram.ArchitectureTests;

// Guards the class of bug behind the orphaned enforcement: a service registered in the container that no
// code path consumes. Consent, block, claim, window and daily-limit were all registered in DI, unit
// tested, and never called, which is how the phase reports read "done". Every registered Hiram service
// must be injected somewhere (a constructor or a method parameter), unless it is on the allowlist, which
// records roadmap work not yet wired. The allowlist shrinks as each enforcement PR lands.
public class DependencyInjectionOrphanTests
{
    // Registered but not yet wired, each with the issue that will wire it and remove the entry.
    private static readonly HashSet<string> Allowlisted = new()
    {
        "Hiram.Application.Messaging.MessageDispatchGuard", // #34 wires the delivery claim
        "Hiram.Application.Scheduling.WindowScheduler",     // #40 models the window and passes dispatch_at
        "Hiram.Application.Scheduling.DailyLimitPolicy",    // #40 daily limit, needs a counter store
        "Hiram.Application.Consents.ConsentReconciler",     // ADR-018 step 2.0, consent dual-write
    };

    [Fact]
    public void EveryRegisteredHiramService_IsConsumed_OrAllowlisted()
    {
        var consumed = CollectConsumedTypes();

        var orphans = BuildContainer()
            .Select(descriptor => descriptor.ServiceType)
            .Where(IsHiramType)
            .Distinct()
            .Where(serviceType => !consumed.Contains(serviceType))
            .Select(serviceType => serviceType.FullName!)
            .Where(name => !Allowlisted.Contains(name))
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            "Registered in DI but consumed by no constructor or method. Wire a call site, or allowlist "
            + $"with the issue that will wire it: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Allowlist_HasNoStaleEntries()
    {
        // Once an allowlisted type gains a call site, its entry is stale and must be removed, otherwise the
        // allowlist rots into a place where real orphans hide.
        var consumed = CollectConsumedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .ToHashSet();

        var stale = Allowlisted
            .Where(consumed.Contains)
            .OrderBy(name => name)
            .ToList();

        Assert.True(stale.Count == 0, $"Allowlisted types that are now wired, remove them: {string.Join(", ", stale)}");
    }

    private static IEnumerable<ServiceDescriptor> BuildContainer()
    {
        // Dummy connection strings and empty configuration: the registrations must not open a socket, so
        // the container can be inspected without Postgres, Redis or a live host.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddHiramInfrastructure("Host=localhost;Database=hiram;Username=hiram;Password=hiram");
        services.AddHiramRedis("localhost:6379");
        services.AddHiramEmailDelivery(configuration);
        services.AddHiramPush(configuration);
        return services;
    }

    private static HashSet<Type> CollectConsumedTypes()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var consumed = new HashSet<Type>();
        foreach (var type in HiramAssemblies().SelectMany(LoadableTypes))
        {
            foreach (var ctor in type.GetConstructors(flags))
                foreach (var parameter in ctor.GetParameters())
                    consumed.Add(parameter.ParameterType);

            foreach (var method in type.GetMethods(flags))
                foreach (var parameter in method.GetParameters())
                    consumed.Add(parameter.ParameterType);
        }

        return consumed;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }

    private static IEnumerable<Assembly> HiramAssemblies()
    {
        yield return typeof(Domain.Notifications.NotificationRequest).Assembly;
        yield return typeof(Application.Notifications.SubmitNotificationHandler).Assembly;
        yield return typeof(DependencyInjection).Assembly;
        yield return typeof(Dispatcher.EmailConsumerWorker).Assembly;
        yield return Assembly.Load("Hiram.Api");
    }

    private static bool IsHiramType(Type type) =>
        type.Namespace is not null && type.Namespace.StartsWith("Hiram.", StringComparison.Ordinal);
}
