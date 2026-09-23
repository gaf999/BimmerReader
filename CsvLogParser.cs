using System;
using System.Collections.Generic;
using System.IO;

namespace BimmerReader
{
    public class SensorInfo
    {
        public string DisplayName { get; init; } = "";
        public string Unit { get; init; } = "";
        public float Min { get; set; }
        public float Max { get; set; }
    }

    public class MergedFrame
    {
        public float Time { get; init; }
        public float[] Values { get; init; } = [];
    }

    public class ParsedLog
    {
        public List<SensorInfo> Sensors { get; } = [];
        public List<MergedFrame> Frames { get; } = [];
        public float Duration => Frames.Count > 0 ? Frames[^1].Time : 0f;
    }

    public static class CsvLogParser
    {
        private static readonly HashSet<string> KnownUnits = new(StringComparer.OrdinalIgnoreCase)
            { "bar", "kpa", "c", "%", "rpm", "v", "a", "ms", "nm", "kw", "hp", "km/h", "mph", "g/s", "mg/stroke" };

        public static ParsedLog Parse(Stream stream)
        {
            var log = new ParsedLog();
            using var reader = new StreamReader(stream);

            string? headerLine = reader.ReadLine();
            if (headerLine == null) return log;

            string[] headers = headerLine.Split(',');
            // index 0 is "Time"
            for (int i = 1; i < headers.Length; i++)
                log.Sensors.Add(ParseHeader(headers[i].Trim()));

            int count = log.Sensors.Count;
            float[] carried = new float[count];
            float lastTime = float.NaN;
            int lastFrameIdx = -1;
            var frames = log.Frames;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split(',');
                if (!float.TryParse(parts[0],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float time)) continue;

                for (int i = 0; i < count && i + 1 < parts.Length; i++)
                    float.TryParse(parts[i + 1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out carried[i]);

                if (time != lastTime)
                {
                    frames.Add(new MergedFrame { Time = time, Values = (float[])carried.Clone() });
                    lastTime = time;
                    lastFrameIdx = frames.Count - 1;
                }
                else
                {
                    // Same timestamp: update last frame in-place (last row wins per timestamp)
                    Array.Copy(carried, frames[lastFrameIdx].Values, count);
                }
            }

            ComputeRanges(log);
            return log;
        }

        private static void ComputeRanges(ParsedLog log)
        {
            int count = log.Sensors.Count;
            if (count == 0 || log.Frames.Count == 0) return;

            float[] mins = new float[count];
            float[] maxs = new float[count];
            Array.Fill(mins, float.MaxValue);
            Array.Fill(maxs, float.MinValue);

            foreach (var frame in log.Frames)
                for (int i = 0; i < count; i++)
                {
                    if (frame.Values[i] < mins[i]) mins[i] = frame.Values[i];
                    if (frame.Values[i] > maxs[i]) maxs[i] = frame.Values[i];
                }

            for (int i = 0; i < count; i++)
            {
                float lo = mins[i], hi = maxs[i];
                if (lo >= hi) { log.Sensors[i].Min = 0; log.Sensors[i].Max = Math.Max(1f, hi + 1f); }
                else
                {
                    float pad = (hi - lo) * 0.05f;
                    log.Sensors[i].Min = (float)Math.Floor(lo - pad);
                    log.Sensors[i].Max = (float)Math.Ceiling(hi + pad);
                }
            }
        }

        private static SensorInfo ParseHeader(string header)
        {
            // "Engine temperature ° C" → unit="°C"
            if (header.EndsWith("° C", StringComparison.Ordinal))
                return new SensorInfo { DisplayName = header[..^3].TrimEnd(), Unit = "°C" };

            int lastSpace = header.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string lastWord = header[(lastSpace + 1)..];
                if (KnownUnits.Contains(lastWord))
                    return new SensorInfo { DisplayName = header[..lastSpace].TrimEnd(), Unit = lastWord };
            }
            return new SensorInfo { DisplayName = header };
        }
    }
}
