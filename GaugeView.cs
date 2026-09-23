using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;
using System;

namespace BimmerReader
{
    public class GaugeView : View
    {
        private Paint _bgPaint = null!;
        private Paint _trackPaint = null!;
        private Paint _arcPaint = null!;
        private Paint _peakArcPaint = null!;
        private Paint _valuePaint = null!;
        private Paint _peakValuePaint = null!;
        private Paint _unitPaint = null!;
        private Paint _namePaint = null!;

        private readonly RectF _arcRect    = new();
        private readonly RectF _peakRect   = new();
        private string _name = "";
        private string _unit = "";
        private string[] _nameLines = [];
        private float _min, _max;
        private float _displayed;
        private float _peak;
        private ValueAnimator? _anim;

        private const float StartAngle = 135f;
        private const float TotalSweep = 270f;

        private static readonly Color AccentColor  = Color.ParseColor("#1C69D4");
        private static readonly Color PeakColor    = Color.ParseColor("#FF4444");
        private static readonly Color BgColor      = Color.ParseColor("#1A1A2E");
        private static readonly Color TrackColor   = Color.ParseColor("#2D2D50");
        private static readonly Color TextColor    = Color.White;
        private static readonly Color UnitColor    = Color.ParseColor("#88AADD");
        private static readonly Color NameColor    = Color.ParseColor("#99AABB");

        public GaugeView(Context ctx) : base(ctx) { InitPaints(); }
        public GaugeView(Context ctx, IAttributeSet? a) : base(ctx, a) { InitPaints(); }

        private void InitPaints()
        {
            _bgPaint = new Paint(PaintFlags.AntiAlias);
            _bgPaint.Color = BgColor;

            _trackPaint = new Paint(PaintFlags.AntiAlias);
            _trackPaint.SetStyle(Paint.Style.Stroke);
            _trackPaint.Color = TrackColor;
            _trackPaint.StrokeCap = Paint.Cap.Round;

            // Peak arc: thin red, drawn before the blue so blue covers it up to current value
            _peakArcPaint = new Paint(PaintFlags.AntiAlias);
            _peakArcPaint.SetStyle(Paint.Style.Stroke);
            _peakArcPaint.Color = PeakColor;
            _peakArcPaint.StrokeCap = Paint.Cap.Round;

            _arcPaint = new Paint(PaintFlags.AntiAlias);
            _arcPaint.SetStyle(Paint.Style.Stroke);
            _arcPaint.Color = AccentColor;
            _arcPaint.StrokeCap = Paint.Cap.Round;

            _valuePaint = new Paint(PaintFlags.AntiAlias);
            _valuePaint.Color = TextColor;
            _valuePaint.TextAlign = Paint.Align.Center;
            _valuePaint.FakeBoldText = true;

            _peakValuePaint = new Paint(PaintFlags.AntiAlias);
            _peakValuePaint.Color = PeakColor;
            _peakValuePaint.TextAlign = Paint.Align.Center;

            _unitPaint = new Paint(PaintFlags.AntiAlias);
            _unitPaint.Color = UnitColor;
            _unitPaint.TextAlign = Paint.Align.Center;

            _namePaint = new Paint(PaintFlags.AntiAlias);
            _namePaint.Color = NameColor;
            _namePaint.TextAlign = Paint.Align.Center;
        }

        public void Configure(string name, string unit, float min, float max)
        {
            _name = name;
            _unit = unit;
            _min = min;
            _max = max > min ? max : min + 1f;
            _displayed = min;
            _peak = min;
            _nameLines = BuildNameLines(name);
            Invalidate();
        }

        public void SetValue(float value, bool animated = true)
        {
            value = Math.Clamp(value, _min, _max);

            // Track peak from the actual target, not the animation position
            if (value > _peak)
            {
                _peak = value;
                Invalidate();
            }

            if (!animated || Math.Abs(value - _displayed) < 0.001f)
            {
                _anim?.Cancel();
                _displayed = value;
                Invalidate();
                return;
            }
            _anim?.Cancel();
            float from = _displayed;
            _anim = ValueAnimator.OfFloat(from, value)!;
            _anim.SetDuration(100);
            _anim.Update += (_, e) =>
            {
                _displayed = ((Java.Lang.Float)e.Animation!.AnimatedValue!).FloatValue();
                Invalidate();
            };
            _anim.Start();
        }

        protected override void OnMeasure(int wSpec, int hSpec)
        {
            int w = MeasureSpec.GetSize(wSpec);
            SetMeasuredDimension(w, w);
        }

        protected override void OnDraw(Canvas canvas)
        {
            float w = Width;
            float cx = w / 2f, cy = w / 2f;
            float outerMargin = w * 0.06f;
            float stroke     = w * 0.09f;
            float inset      = outerMargin + stroke / 2f;

            // Peak arc sits INSIDE the blue ring — shifted toward centre by 0.75 × stroke
            float peakStroke = stroke * 0.30f;
            float peakInset  = inset + stroke * 0.75f;

            _trackPaint.StrokeWidth    = stroke;
            _arcPaint.StrokeWidth      = stroke;
            _peakArcPaint.StrokeWidth  = peakStroke;

            // Background circle
            canvas.DrawCircle(cx, cy, cx - outerMargin + stroke * 0.1f, _bgPaint);

            // Blue arc bounding rect (outer ring)
            _arcRect.Set(inset, inset, w - inset, w - inset);

            // 1. Gray track (full 270°)
            canvas.DrawArc(_arcRect, StartAngle, TotalSweep, false, _trackPaint);

            // 2. Blue current arc (outer ring)
            float fraction = Math.Clamp((_displayed - _min) / (_max - _min), 0f, 1f);
            float sweep    = fraction * TotalSweep;
            if (sweep > 0.5f)
                canvas.DrawArc(_arcRect, StartAngle, sweep, false, _arcPaint);

            // 3. Red peak arc — drawn on a smaller rect so it sits inside the blue ring
            _peakRect.Set(peakInset, peakInset, w - peakInset, w - peakInset);
            float peakFraction = Math.Clamp((_peak - _min) / (_max - _min), 0f, 1f);
            float peakSweep    = peakFraction * TotalSweep;
            if (peakSweep > 0.5f)
                canvas.DrawArc(_peakRect, StartAngle, peakSweep, false, _peakArcPaint);

            // Peak value — halfway between the gauge top and the current value
            _peakValuePaint.TextSize = w * 0.060f;
            canvas.DrawText(FormatValue(_peak), cx, cy - w * 0.18f, _peakValuePaint);

            // Current value — large white, pushed toward centre
            _valuePaint.TextSize = w * 0.21f;
            canvas.DrawText(FormatValue(_displayed), cx, cy + w * 0.05f, _valuePaint);

            // Unit — below current value
            _unitPaint.TextSize = w * 0.085f;
            canvas.DrawText(_unit, cx, cy + w * 0.25f, _unitPaint);

            // Sensor name — bottom, up to 2 lines
            _namePaint.TextSize = w * 0.060f;
            float lineH     = w * 0.075f;
            float totalNameH = (_nameLines.Length - 1) * lineH;
            float nameY     = w - outerMargin * 1.6f - totalNameH;
            for (int i = 0; i < _nameLines.Length; i++)
                canvas.DrawText(_nameLines[i], cx, nameY + i * lineH, _namePaint);
        }

        protected override void OnDetachedFromWindow()
        {
            base.OnDetachedFromWindow();
            _anim?.Cancel();
            _anim = null;
        }

        private static string FormatValue(float v) =>
            Math.Abs(v) >= 1000f ? v.ToString("F0") :
            Math.Abs(v) >= 100f  ? v.ToString("F1") : v.ToString("F2");

        private static string[] BuildNameLines(string name)
        {
            if (name.Length <= 20) return [name];
            int mid = name.Length / 2;
            for (int i = mid; i >= 1; i--)
                if (name[i] == ' ')
                    return [name[..i].Trim(), name[(i + 1)..].Trim()];
            for (int i = mid; i < name.Length; i++)
                if (name[i] == ' ')
                    return [name[..i].Trim(), name[(i + 1)..].Trim()];
            return [name];
        }
    }
}
