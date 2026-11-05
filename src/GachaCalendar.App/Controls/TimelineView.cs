using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using GachaCalendar.App.ViewModels;
using GachaCalendar.Core;

namespace GachaCalendar.App.Controls;

/// <summary>
/// Gantt-style view of all games: one row group per game, bars from start to end.
/// Wheel scrolls rows, Shift+wheel or drag pans days, Ctrl+wheel zooms.
/// </summary>
public sealed class TimelineView : Control
{
    public static readonly StyledProperty<IReadOnlyList<TimelineRow>?> RowsProperty =
        AvaloniaProperty.Register<TimelineView, IReadOnlyList<TimelineRow>?>(nameof(Rows));

    public static readonly StyledProperty<double> DayWidthProperty =
        AvaloniaProperty.Register<TimelineView, double>(nameof(DayWidth), 30, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<EntryItem?> SelectedProperty =
        AvaloniaProperty.Register<TimelineView, EntryItem?>(nameof(Selected), defaultBindingMode: BindingMode.TwoWay);

    private const double LabelWidth = 168;
    private const double HeaderHeight = 58;
    private const double LaneHeight = 28;
    private const double BarHeight = 22;
    private const double RowPadding = 8;
    private const double MinDayWidth = 8;
    private const double MaxDayWidth = 90;

    private readonly DateTime origin = DateTime.Today.AddDays(-60);
    private double offsetDays = 50;
    private double offsetY;
    private Point? pressPoint;
    private double pressDays, pressY;
    private bool dragging;
    private EntryItem? hovered;
    private readonly List<(Rect Rect, EntryItem Item)> hits = [];

    static TimelineView()
    {
        AffectsRender<TimelineView>(RowsProperty, DayWidthProperty, SelectedProperty);
        FocusableProperty.OverrideDefaultValue<TimelineView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<TimelineView>(true);
    }

    public IReadOnlyList<TimelineRow>? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public double DayWidth { get => GetValue(DayWidthProperty); set => SetValue(DayWidthProperty, value); }
    public EntryItem? Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }

    private double BodyWidth => Math.Max(0, Bounds.Width - LabelWidth);
    private double VisibleDays => BodyWidth / DayWidth;
    private double X(DateTime day) => LabelWidth + ((day - origin).TotalDays - offsetDays) * DayWidth;

    /// <summary>Scrolls so today sits a quarter into the visible range.</summary>
    public void GoToday()
    {
        offsetDays = (DateTime.Today - origin).TotalDays - VisibleDays * 0.25;
        InvalidateVisual();
    }

    public void Pan(double days)
    {
        offsetDays += days;
        InvalidateVisual();
    }

    public void Zoom(double factor) => SetZoom(DayWidth * factor, LabelWidth + BodyWidth / 2);

    private void SetZoom(double width, double anchorX)
    {
        width = Math.Clamp(width, MinDayWidth, MaxDayWidth);
        var dayAtAnchor = offsetDays + (anchorX - LabelWidth) / DayWidth;
        DayWidth = width;
        offsetDays = dayAtAnchor - (anchorX - LabelWidth) / width;
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
        if (change.Property == RowsProperty) ClampY();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.PreviousSize.Width == 0) GoToday();
    }

    // ---- rendering ----

    private IBrush Res(string key, string fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : new SolidColorBrush(Color.Parse(fallback));

    private Typeface Face(FontWeight weight = FontWeight.Normal) => new(GetValue(TextElement.FontFamilyProperty), FontStyle.Normal, weight);

    private FormattedText Label(string text, double size, IBrush brush, double maxWidth = double.PositiveInfinity, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face(weight), size, brush)
        { MaxTextWidth = Math.Max(1, maxWidth), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };

    private double RowHeight(TimelineRow row) => Math.Max(1, row.Lanes) * LaneHeight + RowPadding * 2;

    private double ContentHeight => Rows?.Sum(RowHeight) ?? 0;

    private void ClampY() => offsetY = Math.Clamp(offsetY, 0, Math.Max(0, ContentHeight - (Bounds.Height - HeaderHeight) + 20));

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        var panel = Res("GcPanel", "#FFFFFF");
        var line = Res("GcLine", "#DDE1EA");
        var grid = new Pen(Res("GcGrid", "#EEF0F5"), 1);
        var text = Res("GcText", "#1D2230");
        var sub = Res("GcSub", "#6A7285");
        var today = DateTime.Today;
        var accent = Res("GcToday", "#E5484D");
        ctx.FillRectangle(panel, bounds);
        hits.Clear();
        ClampY();

        var firstDay = origin.AddDays(Math.Floor(offsetDays));
        var days = (int)Math.Ceiling(VisibleDays) + 2;
        var body = new Rect(LabelWidth, HeaderHeight, BodyWidth, Math.Max(0, bounds.Height - HeaderHeight));

        using (ctx.PushClip(body))
        {
            var weekend = Res("GcWeekend", "#F7F8FB");
            for (var i = 0; i < days; i++)
            {
                var day = firstDay.AddDays(i);
                var x = X(day);
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) ctx.FillRectangle(weekend, new Rect(x, body.Top, DayWidth, body.Height));
                if (day.DayOfWeek == DayOfWeek.Monday || DayWidth >= 26) ctx.DrawLine(day.Day == 1 ? new Pen(line, 1) : grid, new Point(x, body.Top), new Point(x, body.Bottom));
            }
            var todayX = X(today);
            ctx.FillRectangle(new SolidColorBrush(((ISolidColorBrush)accent).Color, 0.08), new Rect(todayX, body.Top, DayWidth, body.Height));

            var y = HeaderHeight - offsetY;
            foreach (var row in Rows ?? [])
            {
                var height = RowHeight(row);
                if (y + height >= body.Top && y <= body.Bottom)
                    foreach (var bar in row.Bars) DrawBar(ctx, bar, y + RowPadding + bar.Lane * LaneHeight, body, text);
                y += height;
                ctx.DrawLine(new Pen(line, 1), new Point(LabelWidth, y), new Point(bounds.Width, y));
            }
            ctx.DrawLine(new Pen(accent, 1.5), new Point(todayX + DayWidth / 2, body.Top), new Point(todayX + DayWidth / 2, body.Bottom));
            if (Rows is not { Count: > 0 })
            {
                var empty = Label("当前筛选下没有活动。可以在左侧添加游戏，或点击“刷新”获取最新公告。", 13, sub, body.Width - 40);
                ctx.DrawText(empty, new Point(body.Left + 20, body.Top + 30));
            }
        }

        DrawHeader(ctx, bounds, firstDay, days, panel, line, text, sub, accent, today);
        DrawLabels(ctx, bounds, panel, line, text, sub);
    }

    private void DrawBar(DrawingContext ctx, TimelineBar bar, double top, Rect body, IBrush text)
    {
        var item = bar.Item;
        var entry = item.Entry;
        var x0 = X(entry.Start) + 1;
        var x1 = X(entry.End.AddDays(1)) - 1;
        if (x1 < body.Left || x0 > body.Right) return;
        var rect = new Rect(x0, top + (LaneHeight - BarHeight) / 2, Math.Max(6, x1 - x0), BarHeight);
        var color = item.KindColor;
        var opacity = item.Done ? 0.35 : 1.0;
        using (ctx.PushOpacity(opacity))
        {
            if (entry.Uncertain)
            {
                ctx.DrawRectangle(new SolidColorBrush(color, 0.22), new Pen(new SolidColorBrush(color), 1.2, new DashStyle([3, 2], 0)), rect, 5, 5);
            }
            else
            {
                ctx.DrawRectangle(new SolidColorBrush(color), null, rect, 5, 5);
                // Elapsed part of a running entry is shaded.
                if (item.Progress is > 0 and < 1)
                    using (ctx.PushClip(new Rect(rect.X, rect.Y, rect.Width * item.Progress, rect.Height)))
                        ctx.DrawRectangle(new SolidColorBrush(Colors.Black, 0.16), null, rect, 5, 5);
            }
            if (item.Starred) ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Palette.Star), 2.2), rect.Inflate(1), 6, 6);
            if (item.IsCollab) ctx.DrawRectangle(new SolidColorBrush(Palette.Collab), null, new Rect(rect.Right - 7, rect.Y, 7, 7), 2, 2);
            if (Selected?.Entry.Key == entry.Key) ctx.DrawRectangle(null, new Pen(text, 2), rect.Inflate(2.5), 7, 7);
            if (item == hovered) ctx.DrawRectangle(new SolidColorBrush(Colors.White, 0.15), null, rect, 5, 5);

            // Labels stay readable while the bar scrolls out on the left.
            var left = Math.Max(rect.Left, body.Left) + 6;
            var width = rect.Right - left - 6;
            if (width > 14)
            {
                var label = (item.Starred ? "★ " : "") + entry.Title + (item.EndingSoon ? $" · {item.Remaining}" : "");
                var brush = entry.Uncertain ? text : Brushes.White;
                var formatted = Label(label, 12, brush, width, item.Starred ? FontWeight.SemiBold : FontWeight.Normal);
                ctx.DrawText(formatted, new Point(left, rect.Top + (BarHeight - formatted.Height) / 2));
            }
        }
        hits.Add((rect.Intersect(body), item));
    }

    private void DrawHeader(DrawingContext ctx, Rect bounds, DateTime firstDay, int days, IBrush panel, IBrush line, IBrush text, IBrush sub, IBrush accent, DateTime today)
    {
        var header = new Rect(0, 0, bounds.Width, HeaderHeight);
        ctx.FillRectangle(panel, header);
        using (ctx.PushClip(new Rect(LabelWidth, 0, BodyWidth, HeaderHeight)))
        {
            for (var i = 0; i < days; i++)
            {
                var day = firstDay.AddDays(i);
                var x = X(day);
                if (day.Day == 1 || i == 0)
                {
                    var month = Label(day.Year == today.Year ? $"{day.Month}月" : $"{day.Year}年{day.Month}月", 12, text, 200, FontWeight.SemiBold);
                    ctx.DrawText(month, new Point(Math.Max(x, LabelWidth) + 4, 5));
                }
                var every = DayWidth >= 18 ? 1 : DayWidth >= 10 ? 7 : 14;
                if ((day - origin).Days % every != 0 && every > 1 && day.DayOfWeek != DayOfWeek.Monday) continue;
                if (every > 1 && day.DayOfWeek != DayOfWeek.Monday) continue;
                var isToday = day == today;
                var number = Label(day.Day.ToString(CultureInfo.InvariantCulture), 11, isToday ? Brushes.White : sub, DayWidth * every, isToday ? FontWeight.SemiBold : FontWeight.Normal);
                var cx = x + DayWidth / 2;
                if (isToday) ctx.DrawEllipse(accent, null, new Point(cx, 43), 10, 10);
                ctx.DrawText(number, new Point(cx - number.Width / 2, 43 - number.Height / 2));
                if (DayWidth >= 24)
                {
                    var weekday = Label("日一二三四五六"[(int)day.DayOfWeek].ToString(), 9, sub);
                    ctx.DrawText(weekday, new Point(cx - weekday.Width / 2, 25));
                }
            }
        }
        ctx.DrawLine(new Pen(line, 1), new Point(0, HeaderHeight - 0.5), new Point(bounds.Width, HeaderHeight - 0.5));
    }

    private void DrawLabels(DrawingContext ctx, Rect bounds, IBrush panel, IBrush line, IBrush text, IBrush sub)
    {
        ctx.FillRectangle(panel, new Rect(0, HeaderHeight, LabelWidth, Math.Max(0, bounds.Height - HeaderHeight)));
        ctx.DrawText(Label("游戏 / 区服", 11, sub), new Point(14, 22));
        using (ctx.PushClip(new Rect(0, HeaderHeight, LabelWidth, Math.Max(0, bounds.Height - HeaderHeight))))
        {
            var y = HeaderHeight - offsetY;
            foreach (var row in Rows ?? [])
            {
                var height = RowHeight(row);
                ctx.FillRectangle(new SolidColorBrush(row.Game.Color), new Rect(0, y + 6, 4, height - 12), 2);
                ctx.DrawText(Label(row.Game.Name, 13, text, LabelWidth - 24, FontWeight.SemiBold), new Point(14, y + 9));
                ctx.DrawText(Label($"{row.Game.Region} · {row.Bars.Count} 条", 11, sub, LabelWidth - 24), new Point(14, y + 29));
                y += height;
                ctx.DrawLine(new Pen(line, 1), new Point(0, y), new Point(LabelWidth, y));
            }
        }
        ctx.DrawLine(new Pen(line, 1), new Point(LabelWidth - 0.5, 0), new Point(LabelWidth - 0.5, bounds.Height));
    }

    // ---- input ----

    private EntryItem? HitTest(Point point)
    {
        for (var i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Rect.Contains(point)) return hits[i].Item;
        return null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var point = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            SetZoom(DayWidth * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), Math.Max(point.X, LabelWidth));
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
            offsetDays -= (Math.Abs(e.Delta.X) > 0 ? e.Delta.X : e.Delta.Y) * 60 / DayWidth;
        else
        {
            offsetY -= e.Delta.Y * 60;
            ClampY();
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressPoint = e.GetPosition(this);
        pressDays = offsetDays;
        pressY = offsetY;
        dragging = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (pressPoint is { } start)
        {
            var delta = point - start;
            if (!dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) > 4) dragging = true;
            if (dragging)
            {
                offsetDays = pressDays - delta.X / DayWidth;
                offsetY = pressY - delta.Y;
                ClampY();
                Cursor = new Cursor(StandardCursorType.SizeAll);
                InvalidateVisual();
                return;
            }
        }
        var hit = HitTest(point);
        if (hit == hovered) return;
        hovered = hit;
        Cursor = hit is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, hit is null ? null : $"{hit.Title}\n{hit.GameName}（{hit.Region}） · {hit.Kind}{(hit.IsCollab ? " · 联动" : "")}\n{hit.Dates}\n{hit.Remaining}");
        ToolTip.SetIsOpen(this, hit is not null);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressPoint is not null && !dragging) Selected = HitTest(e.GetPosition(this));
        pressPoint = null;
        dragging = false;
        Cursor = Cursor.Default;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hovered = null;
        ToolTip.SetIsOpen(this, false);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left: Pan(-7); break;
            case Key.Right: Pan(7); break;
            case Key.Home: GoToday(); break;
            case Key.OemPlus or Key.Add: Zoom(1.2); break;
            case Key.OemMinus or Key.Subtract: Zoom(1 / 1.2); break;
            case Key.Escape: Selected = null; break;
            default: return;
        }
        e.Handled = true;
    }
}
