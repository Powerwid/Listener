using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using DrawingPath = Android.Graphics.Path;

namespace TonysGo.YapeListener.Android;

internal enum ListenerGlyph { Check, Alert, Home, History, Settings, Reader, Internet, Cloud, Qr, Bell, Battery, Refresh }

internal static class ListenerUi
{
    public static readonly Color Background = Color.Rgb(244, 247, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color Ink = Color.Rgb(23, 42, 60);
    public static readonly Color Muted = Color.Rgb(83, 103, 122);
    public static readonly Color Border = Color.Rgb(218, 227, 235);
    public static readonly Color Brand = Color.Rgb(18, 63, 103);
    public static readonly Color BrandSoft = Color.Rgb(229, 239, 249);
    public static readonly Color Green = Color.Rgb(18, 111, 74);
    public static readonly Color GreenSoft = Color.Rgb(230, 245, 236);
    public static readonly Color Amber = Color.Rgb(137, 78, 5);
    public static readonly Color AmberSoft = Color.Rgb(255, 242, 215);
    public static readonly Color Red = Color.Rgb(168, 43, 49);
    public static readonly Color RedSoft = Color.Rgb(255, 234, 234);

    public static int Dp(Context context, float value) => (int)(value * context.Resources!.DisplayMetrics!.Density + .5f);

    public static GradientDrawable Shape(Context context, Color fill, int radius = 18, bool border = false)
    {
        var shape = new GradientDrawable();
        shape.SetColor(fill);
        shape.SetCornerRadius(Dp(context, radius));
        if (border) shape.SetStroke(Dp(context, 1), Border);
        return shape;
    }

    public static TextView Text(Context context, string text, float size = 15, bool bold = false, Color? color = null)
    {
        var view = new TextView(context) { Text = text, TextSize = size };
        view.SetTextColor(color ?? Ink);
        if (bold) view.SetTypeface(null, TypefaceStyle.Bold);
        view.SetLineSpacing(Dp(context, 2), 1);
        return view;
    }

    public static LinearLayout Column(Context context) => new(context) { Orientation = global::Android.Widget.Orientation.Vertical };
    public static LinearLayout Row(Context context)
    {
        var row = new LinearLayout(context) { Orientation = global::Android.Widget.Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical); return row;
    }

    public static LinearLayout Card(Context context)
    {
        var card = Column(context);
        var padding = Dp(context, 17);
        card.SetPadding(padding, padding, padding, padding);
        card.Background = Shape(context, Surface, 20, true);
        return card;
    }

    public static Button Button(Context context, string text, bool primary = false)
    {
        var button = new Button(context) { Text = text, Gravity = GravityFlags.Center };
        button.SetAllCaps(false);
        button.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 14);
        button.SetTypeface(null, TypefaceStyle.Bold);
        button.SetTextColor(primary ? Surface : Brand);
        button.SetPadding(Dp(context, 14), Dp(context, 12), Dp(context, 14), Dp(context, 12));
        button.SetMinHeight(Dp(context, 48));
        button.SetMinimumHeight(Dp(context, 48));
        button.Background = new RippleDrawable(ColorStateList.ValueOf(primary ? Color.Rgb(63, 100, 135) : BrandSoft),
            Shape(context, primary ? Brand : Surface, 13, !primary), null);
        button.StateListAnimator = null;
        return button;
    }

    public static void Add(LinearLayout parent, View child, int top = 0, int bottom = 0)
    {
        parent.AddView(child, new LinearLayout.LayoutParams(-1, -2)
        {
            TopMargin = Dp(parent.Context!, top), BottomMargin = Dp(parent.Context!, bottom)
        });
    }
}

// Small native vector marks: no emoji font, bitmap downloads or additional UI dependencies.
internal sealed class ListenerIcon : View
{
    private ListenerGlyph _glyph;
    private Color _color;
    public ListenerIcon(Context context, ListenerGlyph glyph, Color color) : base(context)
    {
        _glyph = glyph; _color = color;
        ImportantForAccessibility = ImportantForAccessibility.No;
    }
    public void Show(ListenerGlyph glyph, Color color)
    {
        if (_glyph == glyph && _color == color) return;
        _glyph = glyph; _color = color; Invalidate();
    }
    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas is null) return;
        base.OnDraw(canvas);
        using var paint = new Paint(PaintFlags.AntiAlias) { Color = _color, StrokeWidth = 1.9f, StrokeCap = Paint.Cap.Round, StrokeJoin = Paint.Join.Round };
        paint.SetStyle(Paint.Style.Stroke);
        canvas.Save(); canvas.Translate(PaddingLeft, PaddingTop);
        canvas.Scale((Width - PaddingLeft - PaddingRight) / 24f, (Height - PaddingTop - PaddingBottom) / 24f);
        void Line(float x1, float y1, float x2, float y2) => canvas.DrawLine(x1, y1, x2, y2, paint);
        void Lines(params float[] points)
        {
            using var path = new DrawingPath(); path.MoveTo(points[0], points[1]);
            for (var i = 2; i < points.Length; i += 2) path.LineTo(points[i], points[i + 1]);
            canvas.DrawPath(path, paint);
        }
        switch (_glyph)
        {
            case ListenerGlyph.Check:
                canvas.DrawCircle(12, 12, 9, paint); Lines(8, 12, 11, 15, 16, 9); break;
            case ListenerGlyph.Alert:
                canvas.DrawCircle(12, 12, 9, paint); Line(12, 7, 12, 12); Line(12, 16, 12, 16.1f); break;
            case ListenerGlyph.Home:
                Lines(3, 11, 12, 3, 21, 11); Lines(5, 10, 5, 21, 10, 21, 10, 15, 14, 15, 14, 21, 19, 21, 19, 10); break;
            case ListenerGlyph.History:
                canvas.DrawArc(new RectF(3, 3, 21, 21), 210, 320, false, paint); Lines(3, 3, 3, 9, 8, 9); Lines(12, 7, 12, 12, 16, 14); break;
            case ListenerGlyph.Settings:
                canvas.DrawCircle(12, 12, 6, paint); canvas.DrawCircle(12, 12, 2, paint);
                for (var i = 0; i < 8; i++) { var a = i * Math.PI / 4; Line(12 + 8 * (float)Math.Cos(a), 12 + 8 * (float)Math.Sin(a), 12 + 10 * (float)Math.Cos(a), 12 + 10 * (float)Math.Sin(a)); }
                break;
            case ListenerGlyph.Reader:
                canvas.DrawRoundRect(new RectF(4, 3, 20, 21), 2, 2, paint); Line(8, 8, 16, 8); Line(8, 12, 13, 12); Lines(9, 16, 11, 18, 16, 14); break;
            case ListenerGlyph.Internet:
                canvas.DrawArc(new RectF(1, 4, 23, 26), 225, 90, false, paint);
                canvas.DrawArc(new RectF(5, 9, 19, 23), 225, 90, false, paint);
                canvas.DrawArc(new RectF(9, 14, 15, 20), 225, 90, false, paint); canvas.DrawCircle(12, 20, .7f, paint); break;
            case ListenerGlyph.Cloud:
                using (var path = new DrawingPath())
                {
                    path.MoveTo(6, 18); path.CubicTo(0, 18, 0, 9, 7, 9); path.CubicTo(8, 1, 19, 2, 20, 10);
                    path.CubicTo(25, 11, 23, 18, 19, 18); path.LineTo(6, 18); canvas.DrawPath(path, paint);
                }
                break;
            case ListenerGlyph.Qr:
                canvas.DrawRect(3, 3, 9, 9, paint); canvas.DrawRect(15, 3, 21, 9, paint); canvas.DrawRect(3, 15, 9, 21, paint);
                Lines(15, 15, 18, 15, 18, 18, 21, 18, 21, 21); Line(15, 19, 15, 21); Line(21, 13, 21, 14); break;
            case ListenerGlyph.Bell:
                using (var path = new DrawingPath()) { path.MoveTo(5, 17); path.LineTo(7, 14); path.LineTo(7, 9); path.CubicTo(7, 2, 17, 2, 17, 9); path.LineTo(17, 14); path.LineTo(19, 17); path.Close(); canvas.DrawPath(path, paint); }
                canvas.DrawArc(new RectF(9, 17, 15, 23), 20, 140, false, paint); break;
            case ListenerGlyph.Battery:
                canvas.DrawRoundRect(new RectF(2, 6, 19, 18), 2, 2, paint); Line(22, 10, 22, 14); Lines(7, 10, 11, 10, 9, 14, 14, 14); break;
            case ListenerGlyph.Refresh:
                canvas.DrawArc(new RectF(3, 3, 21, 21), 30, 290, false, paint); Lines(16, 3, 21, 3, 21, 8); break;
        }
        canvas.Restore();
    }
}

internal sealed class ListenerCheckRow : LinearLayout
{
    private readonly ListenerIcon _mark;
    private readonly TextView _detail;
    private string? _previous;
    public ListenerCheckRow(Context context, ListenerGlyph glyph, string title) : base(context)
    {
        Orientation = global::Android.Widget.Orientation.Horizontal; SetGravity(GravityFlags.CenterVertical);
        SetPadding(0, ListenerUi.Dp(context, 12), 0, ListenerUi.Dp(context, 12));
        var symbol = new ListenerIcon(context, glyph, ListenerUi.Brand);
        AddView(symbol, new LayoutParams(ListenerUi.Dp(context, 23), ListenerUi.Dp(context, 23)) { RightMargin = ListenerUi.Dp(context, 12) });
        var text = ListenerUi.Column(context);
        text.AddView(ListenerUi.Text(context, title, 15, true));
        _detail = ListenerUi.Text(context, "Comprobando…", 13, color: ListenerUi.Muted); text.AddView(_detail);
        AddView(text, new LayoutParams(0, -2, 1));
        _mark = new ListenerIcon(context, ListenerGlyph.Refresh, ListenerUi.Muted);
        AddView(_mark, new LayoutParams(ListenerUi.Dp(context, 23), ListenerUi.Dp(context, 23)) { LeftMargin = ListenerUi.Dp(context, 8) });
    }
    public void Update(string detail, Color color, bool good = false, bool checking = false)
    {
        var signature = $"{detail}|{color}|{good}|{checking}";
        if (signature == _previous) return;
        _previous = signature; _detail.Text = detail; _detail.SetTextColor(color);
        _mark.Show(good ? ListenerGlyph.Check : checking ? ListenerGlyph.Refresh : ListenerGlyph.Alert, color);
    }
}
