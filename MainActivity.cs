using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Text.Util;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;

namespace BimmerReader
{
    [Activity(Label = "@string/app_name", ScreenOrientation = ScreenOrientation.Portrait)]
    public class MainActivity : Activity
    {
        private const int PickFileCode = 1001;

        private static readonly float[] Speeds = { 1f, 2f, 4f, 0.5f };
        private int _speedIdx = 0;

        private enum ViewMode { Gauges, Values }
        private ViewMode _viewMode = ViewMode.Gauges;

        private ParsedLog? _log;
        private PlaybackEngine? _engine;
        private bool _userSeeking = false;
        private AlertDialog? _loadingDialog;

        // Gauge views
        private readonly List<GaugeView> _gaugeViews = [];

        // Value row views
        private sealed class SensorRow
        {
            public TextView Name = null!;
            public TextView Peak = null!;
            public TextView Value = null!;
            public TextView Unit = null!;
            public float PeakValue;
        }
        private readonly List<SensorRow> _rowViews = [];

        // Layout views
        private LinearLayout _panelLoad = null!;
        private ScrollView _scrollGauges = null!;
        private LinearLayout _gaugeContainer = null!;
        private ScrollView _scrollValues = null!;
        private LinearLayout _valuesContainer = null!;
        private LinearLayout _panelPlayback = null!;
        private SeekBar _seekBar = null!;
        private TextView _tvTime = null!;
        private Button _btnLoad = null!;
        private Button _btnOpen = null!;
        private Button _btnReset = null!;
        private Button _btnPlay = null!;
        private Button _btnSpeed = null!;
        private Button _btnMenu = null!;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            ActionBar?.Hide();
            SetContentView(Resource.Layout.activity_main);

            _panelLoad      = FindViewById<LinearLayout>(Resource.Id.panel_load)!;
            _scrollGauges   = FindViewById<ScrollView>(Resource.Id.scroll_gauges)!;
            _gaugeContainer = FindViewById<LinearLayout>(Resource.Id.gauge_container)!;
            _scrollValues   = FindViewById<ScrollView>(Resource.Id.scroll_values)!;
            _valuesContainer = FindViewById<LinearLayout>(Resource.Id.values_container)!;
            _panelPlayback  = FindViewById<LinearLayout>(Resource.Id.panel_playback)!;
            _seekBar        = FindViewById<SeekBar>(Resource.Id.seek_bar)!;
            _tvTime         = FindViewById<TextView>(Resource.Id.tv_time)!;
            _btnLoad        = FindViewById<Button>(Resource.Id.btn_load)!;
            _btnOpen        = FindViewById<Button>(Resource.Id.btn_open)!;
            _btnReset       = FindViewById<Button>(Resource.Id.btn_reset)!;
            _btnPlay        = FindViewById<Button>(Resource.Id.btn_play)!;
            _btnSpeed       = FindViewById<Button>(Resource.Id.btn_speed)!;
            _btnMenu        = FindViewById<Button>(Resource.Id.btn_menu)!;

            _btnOpen.Text  = "📂";
            _btnReset.Text = "⏮";
            _btnPlay.Text  = "▶";
            _btnSpeed.Text = "1×";
            _btnMenu.Text  = "⋮";

            _btnLoad.Click  += (_, _) => LaunchFilePicker();
            _btnOpen.Click  += (_, _) => LaunchFilePicker();
            _btnReset.Click += (_, _) => { _engine?.Reset(); UpdatePlayButton(); };
            _btnPlay.Click  += (_, _) => TogglePlayback();
            _btnSpeed.Click += (_, _) => CycleSpeed();
            _btnMenu.Click  += (_, _) => ShowOverflowMenu();

            _seekBar.StartTrackingTouch += (_, _) =>
            {
                _userSeeking = true;
                _engine?.Pause();
                UpdatePlayButton();
            };
            _seekBar.StopTrackingTouch += (_, _) => { _userSeeking = false; };
            _seekBar.ProgressChanged   += (_, e) =>
            {
                if (e.FromUser && _engine != null)
                    _engine.SeekToFrame(e.Progress);
            };
        }

        protected override void OnDestroy()
        {
            _engine?.Destroy();
            base.OnDestroy();
        }

        // ── File picker ─────────────────────────────────────────────────────

        private void LaunchFilePicker()
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            StartActivityForResult(intent, PickFileCode);
        }

#pragma warning disable CA1422
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode != PickFileCode || resultCode != Result.Ok || data?.Data == null)
                return;

            var uri = data.Data!;
            try { ContentResolver!.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission); }
            catch { }

            // Show spinner then parse off the UI thread (large files take 200-500 ms)
            ShowLoadingDialog();
            _ = ParseAndBuildAsync(uri);
        }
#pragma warning restore CA1422

        // ── Async load ───────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task ParseAndBuildAsync(Android.Net.Uri uri)
        {
            ParsedLog? log = null;
            try
            {
                // Read bytes on UI thread (SAF stream must stay on calling thread for some providers)
                byte[] bytes;
                using (var stream = ContentResolver!.OpenInputStream(uri)!)
                {
                    using var ms = new System.IO.MemoryStream();
                    await stream.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }

                // Parse on background thread
                log = await System.Threading.Tasks.Task.Run(() =>
                    CsvLogParser.Parse(new System.IO.MemoryStream(bytes)));
            }
            catch (Exception ex)
            {
                RunOnUiThread(() => { DismissLoadingDialog(); ShowToast($"Error: {ex.Message}"); });
                return;
            }

            if (log == null || log.Frames.Count == 0 || log.Sensors.Count == 0)
            {
                RunOnUiThread(() => { DismissLoadingDialog(); ShowToast("No data found in file."); });
                return;
            }

            RunOnUiThread(() => { DismissLoadingDialog(); _log = log; BuildViews(); });
        }

        // ── Build both views ─────────────────────────────────────────────────

        private void BuildViews()
        {
            _engine?.Destroy();
            _engine = null;
            UpdatePlayButton();

            BuildGaugeGrid();
            BuildValueRows();

            _engine = new PlaybackEngine(_log!);
            _engine.FrameChanged     += OnFrameChanged;
            _engine.PlayStateChanged += OnPlayStateChanged;

            _seekBar.Max  = Math.Max(1, _log!.Frames.Count - 1);
            _speedIdx     = 0;
            _btnSpeed.Text = "1×";

            if (_log.Frames.Count > 0)
                OnFrameChanged(0, _log.Frames[0].Values);

            // Show correct content panel
            _panelLoad.Visibility = ViewStates.Gone;
            ApplyViewMode();
            _panelPlayback.Visibility = ViewStates.Visible;
        }

        private void BuildGaugeGrid()
        {
            _gaugeViews.Clear();
            _gaugeContainer.RemoveAllViews();

            int screenW   = Resources!.DisplayMetrics!.WidthPixels;
            int gaugeSize = screenW / 2;
            var sensors   = _log!.Sensors;

            for (int i = 0; i < sensors.Count; i += 2)
            {
                var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
                for (int j = i; j < Math.Min(i + 2, sensors.Count); j++)
                {
                    var g = new GaugeView(this);
                    g.Configure(sensors[j].DisplayName, sensors[j].Unit, sensors[j].Min, sensors[j].Max);
                    g.LayoutParameters = new LinearLayout.LayoutParams(gaugeSize, gaugeSize);
                    _gaugeViews.Add(g);
                    row.AddView(g);
                }
                _gaugeContainer.AddView(row, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            }
        }

        private void BuildValueRows()
        {
            _rowViews.Clear();
            _valuesContainer.RemoveAllViews();
            var sensors = _log!.Sensors;

            for (int i = 0; i < sensors.Count; i++)
            {
                // Separator line (skip first)
                if (i > 0)
                {
                    var sep = new View(this);
                    sep.SetBackgroundColor(Android.Graphics.Color.ParseColor("#1E1E32"));
                    sep.LayoutParameters = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MatchParent, 1);
                    _valuesContainer.AddView(sep);
                }

                var rowView = LayoutInflater!.Inflate(Resource.Layout.item_sensor_row, _valuesContainer, false)!;
                var sr = new SensorRow
                {
                    Name  = rowView.FindViewById<TextView>(Resource.Id.tv_row_name)!,
                    Peak  = rowView.FindViewById<TextView>(Resource.Id.tv_row_peak)!,
                    Value = rowView.FindViewById<TextView>(Resource.Id.tv_row_value)!,
                    Unit  = rowView.FindViewById<TextView>(Resource.Id.tv_row_unit)!,
                    PeakValue = sensors[i].Min,
                };
                sr.Name.Text = sensors[i].DisplayName;
                sr.Unit.Text = sensors[i].Unit;
                sr.Peak.Text = "";

                // Alternate row background
                rowView.SetBackgroundColor(Android.Graphics.Color.ParseColor(
                    i % 2 == 0 ? "#111122" : "#161628"));

                _rowViews.Add(sr);
                _valuesContainer.AddView(rowView);
            }
        }

        // ── Playback callbacks ───────────────────────────────────────────────

        private void OnFrameChanged(int frameIdx, float[] values)
        {
            RunOnUiThread(() =>
            {
                // Update gauges
                for (int i = 0; i < Math.Min(_gaugeViews.Count, values.Length); i++)
                    _gaugeViews[i].SetValue(values[i]);

                // Update value rows + track peak
                for (int i = 0; i < Math.Min(_rowViews.Count, values.Length); i++)
                {
                    var sr = _rowViews[i];
                    float v = values[i];
                    if (v > sr.PeakValue) { sr.PeakValue = v; sr.Peak.Text = FormatVal(v); }
                    sr.Value.Text = FormatVal(v);
                }

                if (!_userSeeking)
                    _seekBar.Progress = frameIdx;

                float cur = _log!.Frames[frameIdx].Time;
                _tvTime.Text = $"{cur:F1}s / {_log.Duration:F1}s";
            });
        }

        private void OnPlayStateChanged(bool playing) =>
            RunOnUiThread(UpdatePlayButton);

        // ── Controls ─────────────────────────────────────────────────────────

        private void TogglePlayback()
        {
            if (_engine == null) return;
            if (_engine.IsPlaying) _engine.Pause();
            else _engine.Play();
            UpdatePlayButton();
        }

        private void UpdatePlayButton() =>
            _btnPlay.Text = (_engine?.IsPlaying ?? false) ? "⏸" : "▶";

        private void CycleSpeed()
        {
            _speedIdx = (_speedIdx + 1) % Speeds.Length;
            float s = Speeds[_speedIdx];
            _engine?.SetSpeed(s);
            _btnSpeed.Text = s < 1f ? $"{s}×" : $"{(int)s}×";
        }

        // ── Menu ─────────────────────────────────────────────────────────────

        private void ShowOverflowMenu()
        {
            var popup = new PopupMenu(this, _btnMenu);
            popup.Menu?.Add(0, 1, 0, "Gauges");
            popup.Menu?.Add(0, 2, 0, "Values");
            popup.Menu?.Add(0, 3, 0, "About");
            popup.MenuItemClick += (_, e) =>
            {
                switch (e.Item?.ItemId)
                {
                    case 1: _viewMode = ViewMode.Gauges; ApplyViewMode(); break;
                    case 2: _viewMode = ViewMode.Values; ApplyViewMode(); break;
                    case 3: ShowAbout(); break;
                }
            };
            popup.Show();
        }

        private void ApplyViewMode()
        {
            if (_log == null) return;
            _scrollGauges.Visibility = _viewMode == ViewMode.Gauges ? ViewStates.Visible : ViewStates.Gone;
            _scrollValues.Visibility = _viewMode == ViewMode.Values ? ViewStates.Visible : ViewStates.Gone;
        }

        private void ShowAbout()
        {
            var view = LayoutInflater!.Inflate(Resource.Layout.dialog_about, null)!;

            var tv = view.FindViewById<TextView>(Resource.Id.tv_about_text)!;
            Linkify.AddLinks(tv, MatchOptions.EmailAddresses);
            tv.MovementMethod = Android.Text.Method.LinkMovementMethod.Instance;

            var dialog = new AlertDialog.Builder(this)!
                .SetView(view)!
                .SetPositiveButton("Close", (d, _) => (d as IDialogInterface)?.Dismiss())!
                .Create()!;
            // Transparent window so only our dark custom view is visible (no grey AlertDialog chrome)
            dialog.Window?.SetBackgroundDrawableResource(Android.Resource.Color.Transparent);
            dialog.Show();
        }

        private void ShowLoadingDialog()
        {
            var view = LayoutInflater!.Inflate(Resource.Layout.dialog_loading, null)!;
            _loadingDialog = new AlertDialog.Builder(this)!
                .SetView(view)!
                .SetCancelable(false)!
                .Create()!;
            _loadingDialog.Window?.SetBackgroundDrawableResource(Android.Resource.Color.Transparent);
            _loadingDialog.Show();
        }

        private void DismissLoadingDialog()
        {
            _loadingDialog?.Dismiss();
            _loadingDialog = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string FormatVal(float v) =>
            Math.Abs(v) >= 1000f ? v.ToString("F0") :
            Math.Abs(v) >= 100f  ? v.ToString("F1") : v.ToString("F2");

        private void ShowToast(string msg) =>
            Toast.MakeText(this, msg, ToastLength.Long)!.Show();
    }
}
