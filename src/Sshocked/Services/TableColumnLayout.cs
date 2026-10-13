using Spectre.Console;
using Spectre.Console.Rendering;

namespace Sshocked.Services;

/// <summary>A single column layout for all tables in one visible server view.</summary>
internal sealed class TableColumnLayout(int[] widths)
{
    internal static TableColumnLayout Measure(IReadOnlyList<Table> tables, RenderOptions options, int? availableWidth = null)
    {
        if (tables.Count == 0) return new([]);

        var template = tables[0];
        var widths = new int[template.Columns.Count];
        var measureWidth = availableWidth ?? int.MaxValue;
        foreach (var table in tables)
        {
            for (var index = 0; index < widths.Length; index++)
            {
                var column = table.Columns[index];
                widths[index] = Math.Max(widths[index], column.Header.Measure(options, measureWidth).Max);
                foreach (var row in table.Rows)
                    widths[index] = Math.Max(widths[index], row[index].Measure(options, measureWidth).Max);
                widths[index] = Math.Max(1, widths[index]);
            }
        }

        if (availableWidth is { } viewport)
        {
            // Match Spectre's accounting for the actual border and horizontal padding.
            var border = template.Border.GetSafeBorder(!options.Unicode && template.UseSafeBorder);
            var overhead = border.Visible ? widths.Length - 1 : 0;
            if (border.UsePadding)
            {
                overhead += border.Visible ? 2 : 0;
                overhead += template.Columns.Sum(column => column.Padding?.GetWidth() ?? 0);
            }
            var budget = Math.Max(widths.Length, viewport - overhead);
            Fit(widths, budget, template.Expand);
        }

        return new(widths);
    }

    internal void Apply(Table table)
    {
        // Explicit widths prevent Spectre from remeasuring or expanding individual groups.
        for (var index = 0; index < widths.Length; index++)
            table.Columns[index].Width = widths[index];
    }

    private static void Fit(int[] widths, int budget, bool expand)
    {
        var total = widths.Sum();
        while (total > budget)
        {
            // Reduce the widest columns together until they meet the next widest column.
            // This bounds the work by column count even for unusually long host metadata.
            var largest = widths.Max();
            var widest = Enumerable.Range(0, widths.Length).Where(index => widths[index] == largest).ToArray();
            var next = widths.Where(width => width < largest).DefaultIfEmpty(1).Max();
            var reduction = Math.Min(total - budget, (largest - next) * widest.Length);
            if (reduction == 0) break;
            for (var index = 0; index < widest.Length; index++)
                widths[widest[index]] -= reduction / widest.Length + (index < reduction % widest.Length ? 1 : 0);
            total -= reduction;
        }

        if (!expand || total >= budget) return;
        var extra = budget - total;
        var shares = widths.Select(width => (long)extra * width).ToArray();
        for (var index = 0; index < widths.Length; index++) widths[index] += (int)(shares[index] / total);
        var remainder = budget - widths.Sum();
        foreach (var index in Enumerable.Range(0, widths.Length).OrderByDescending(index => shares[index] % total).Take(remainder))
            widths[index]++;
    }
}
