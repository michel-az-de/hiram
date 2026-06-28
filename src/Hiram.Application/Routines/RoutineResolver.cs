namespace Hiram.Application.Routines;

public sealed class RoutineResolver
{
    private readonly IRoutineCatalog _catalog;
    private readonly ITemplateApprovalLookup _templates;

    public RoutineResolver(IRoutineCatalog catalog, ITemplateApprovalLookup templates)
    {
        _catalog = catalog;
        _templates = templates;
    }

    public async Task<RoutineDecision> ResolveAsync(Guid tenantId, string eventType, CancellationToken cancellationToken)
    {
        var routines = await _catalog.MatchAsync(tenantId, eventType, cancellationToken);
        if (routines.Count == 0)
            return RoutineDecision.NoMatch();

        var fanout = new List<FanoutItem>();
        var suppressed = new List<SuppressedItem>();

        foreach (var routine in routines)
        {
            foreach (var channel in routine.Channels)
            {
                var name = channel.ToString().ToLowerInvariant();
                var approval = await _templates.ForAsync(tenantId, routine.TemplateName, channel, cancellationToken);

                if (!approval.Exists)
                    suppressed.Add(new SuppressedItem(routine, channel, $"template '{routine.TemplateName}' not found for channel {name}"));
                else if (!approval.Approved)
                    suppressed.Add(new SuppressedItem(routine, channel, $"template '{routine.TemplateName}' for channel {name} is not approved"));
                else
                    fanout.Add(new FanoutItem(routine, channel, routine.TemplateName));
            }
        }

        return new RoutineDecision(fanout, suppressed, NoRoute: false);
    }
}
