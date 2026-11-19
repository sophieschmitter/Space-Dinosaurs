using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sound clean-up for the footsteps: spectral noise reduction (the same idea as Audacity's Noise Reduction) using
/// a stretch of pure background as the noise sample, then cutting the cleaned recording into single footsteps.
/// Run from: Tools > Space Dinosaurs > Clean Up Footsteps. Writes new files; the original is never changed.
/// </summary>
public static class AudioCleanup
{
    const string Source = "Assets/SFX/Footsteps.wav";
    const string CleanPath = "Assets/SFX/Footsteps Clean.wav";
    const string StepFolder = "Assets/SFX/Footstep Steps";

    [MenuItem("Tools/Space Dinosaurs/Clean Up Footsteps")]
    public static void Run() => Debug.Log(CleanFootsteps());

    public static string CleanFootsteps(float noiseStart = 0.1f, float noiseEnd = 1.5f, float reductionDb = 6f)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Source);
        int ch = clip.channels, rate = clip.frequency, n = clip.samples;
        var data = new float[n * ch];
        clip.GetData(data, 0);

        var outData = new float[data.Length];
        for (int c = 0; c < ch; c++)
        {
            var mono = new float[n];
            for (int i = 0; i < n; i++) mono[i] = data[i * ch + c];
            var clean = DenoiseSmooth(mono, rate, (int)(noiseStart * rate), (int)(noiseEnd * rate), reductionDb);
            clean = Tone(clean, rate);
            for (int i = 0; i < n; i++) outData[i * ch + c] = clean[i];
        }
        WriteWav(CleanPath, outData, ch, rate);
        AssetDatabase.ImportAsset(CleanPath);

        double before = Rms(data, ch, (int)(noiseStart * rate), (int)(noiseEnd * rate));
        double after = Rms(outData, ch, (int)(noiseStart * rate), (int)(noiseEnd * rate));
        double stepBefore = PeakWindow(data, ch, rate), stepAfter = PeakWindow(outData, ch, rate);
        return $"Footsteps cleaned: background {Db(before):F1} -> {Db(after):F1} dBFS ({Db(before) - Db(after):F1} dB quieter); " +
               $"loudest step {Db(stepBefore):F1} -> {Db(stepAfter):F1} dBFS. Wrote {CleanPath}";
    }

    // ---------- spectral noise reduction ----------
    // Denoise: the first, harsher gate (kept for reference). DenoiseSmooth below is the one used.

    const int Frame = 2048, Hop = Frame / 4;

    static float[] Denoise(float[] x, int rate, int noiseFrom, int noiseTo, float reductionDb)
    {
        int bins = Frame / 2 + 1;
        var window = new float[Frame];
        for (int i = 0; i < Frame; i++) window[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / Frame); // Hann

        // Noise profile: average magnitude per frequency over the background-only stretch.
        var noise = new double[bins];
        int frames = 0;
        for (int start = noiseFrom; start + Frame <= noiseTo; start += Hop, frames++)
        {
            var (re, im) = Spectrum(x, start, window);
            for (int k = 0; k < bins; k++) noise[k] += Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
        }
        for (int k = 0; k < bins; k++) noise[k] /= Math.Max(1, frames);

        // Each frame: bands near the noise level are turned down to the floor, bands well above it are kept.
        // The gain is smoothed over time and neighbouring bands so it doesn't sound watery.
        float floor = Mathf.Pow(10f, -reductionDb / 20f);
        const float threshold = 2.0f;   // a band must be this many times the noise to count as footstep
        var output = new double[x.Length + Frame];
        var norm = new double[x.Length + Frame];
        var prevGain = new double[bins];
        for (int k = 0; k < bins; k++) prevGain[k] = 1;
        for (int start = 0; start < x.Length; start += Hop)
        {
            var (re, im) = Spectrum(x, start, window);
            var gain = new double[bins];
            for (int k = 0; k < bins; k++)
            {
                double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                double snr = noise[k] > 0 ? mag / noise[k] : 1e9;
                // soft knee between "noise" (floor) and "signal" (1)
                double t = Math.Clamp((snr - 1.0) / (threshold * 2 - 1.0), 0, 1);
                gain[k] = floor + (1 - floor) * t * t;
            }
            for (int k = 0; k < bins; k++)
            {
                double g = gain[k];
                if (k > 0) g = Math.Max(g, gain[k - 1] * 0.75);
                if (k < bins - 1) g = Math.Max(g, gain[k + 1] * 0.75);
                // fast attack (steps start instantly), slower release (tails don't get chopped)
                g = g > prevGain[k] ? g : prevGain[k] * 0.6 + g * 0.4;
                prevGain[k] = g;
                re[k] *= g; im[k] *= g;
            }
            var frame = Inverse(re, im);
            for (int i = 0; i < Frame; i++)
            {
                int j = start + i;
                if (j >= x.Length) break;
                output[j] += frame[i] * window[i];
                norm[j] += window[i] * window[i];
            }
        }
        var y = new float[x.Length];
        for (int i = 0; i < x.Length; i++) y[i] = norm[i] > 1e-6 ? (float)(output[i] / norm[i]) : 0f;
        return y;
    }

    // Gentler version (the one in use): a Wiener filter with the "decision-directed" smoothing used in speech
    // clean-up. Each band's gain follows a smoothed estimate of how far above the noise it is, so gains glide
    // instead of flicking on and off frame to frame - that flicker is what sounds watery / like a maraca.
    // Gains are also averaged with neighbouring bands, and never go below the Reduction floor, so a little
    // natural background is left (that keeps the steps sounding real rather than processed).
    public static double Alpha = 0.99; public static int Spread = 6; public static double TimeSmooth = 0.7;
    static float[] DenoiseSmooth(float[] x, int rate, int noiseFrom, int noiseTo, float reductionDb)
    {
        int bins = Frame / 2 + 1;
        var window = new float[Frame];
        for (int i = 0; i < Frame; i++) window[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / Frame);

        var noise = new double[bins]; // noise power per band
        int frames = 0;
        for (int start = noiseFrom; start + Frame <= noiseTo; start += Hop, frames++)
        {
            var (re, im) = Spectrum(x, start, window);
            for (int k = 0; k < bins; k++) noise[k] += re[k] * re[k] + im[k] * im[k];
        }
        for (int k = 0; k < bins; k++) noise[k] = Math.Max(noise[k] / Math.Max(1, frames), 1e-12);

        double floor = Math.Pow(10, -reductionDb / 20.0);
        double alpha = Alpha;          // how much the SNR estimate leans on the last frame (higher = smoother)
        var heldGain = new double[bins];
        for (int k = 0; k < bins; k++) heldGain[k] = 1;
        var output = new double[x.Length + Frame];
        var norm = new double[x.Length + Frame];
        var prevClean = new double[bins]; // last frame's cleaned power
        var gain = new double[bins];
        var smoothGain = new double[bins];
        for (int start = 0; start < x.Length; start += Hop)
        {
            var (re, im) = Spectrum(x, start, window);
            for (int k = 0; k < bins; k++)
            {
                double power = re[k] * re[k] + im[k] * im[k];
                double post = power / noise[k];                                  // a posteriori SNR
                double prio = alpha * prevClean[k] / noise[k] + (1 - alpha) * Math.Max(post - 1, 0); // a priori
                gain[k] = Math.Max(prio / (1 + prio), floor);
            }
            for (int k = 0; k < bins; k++) // average with Spread bands either side
            {
                double sum = 0; int n = 0;
                for (int j = Math.Max(0, k - Spread); j <= Math.Min(bins - 1, k + Spread); j++) { sum += gain[j]; n++; }
                smoothGain[k] = sum / n;
            }
            for (int k = 0; k < bins; k++)
            {
                // opens instantly (step hits stay sharp), closes gradually (no fizz as the gate shuts)
                double g = smoothGain[k] >= heldGain[k] ? smoothGain[k] : heldGain[k] * TimeSmooth + smoothGain[k] * (1 - TimeSmooth);
                heldGain[k] = g;
                prevClean[k] = g * g * (re[k] * re[k] + im[k] * im[k]);
                re[k] *= g; im[k] *= g;
            }
            var frame = Inverse(re, im);
            for (int i = 0; i < Frame; i++)
            {
                int j = start + i;
                if (j >= x.Length) break;
                output[j] += frame[i] * window[i];
                norm[j] += window[i] * window[i];
            }
        }
        var y = new float[x.Length];
        for (int i = 0; i < x.Length; i++) y[i] = norm[i] > 1e-6 ? (float)(output[i] / norm[i]) : 0f;
        return y;
    }

    static (double[] re, double[] im) Spectrum(float[] x, int start, float[] window)
    {
        var re = new double[Frame];
        var im = new double[Frame];
        for (int i = 0; i < Frame; i++)
        {
            int j = start + i;
            re[i] = j < x.Length ? x[j] * window[i] : 0;
        }
        Fft(re, im, false);
        return (re, im);
    }

    // Takes the half spectrum (with matching negative frequencies implied) back to samples.
    static double[] Inverse(double[] re, double[] im)
    {
        for (int k = 1; k < Frame / 2; k++) { re[Frame - k] = re[k]; im[Frame - k] = -im[k]; }
        Fft(re, im, true);
        var o = new double[Frame];
        for (int i = 0; i < Frame; i++) o[i] = re[i] / Frame;
        return o;
    }

    static void Fft(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }

    // ---------- tone ----------

    /// <summary>Gentle EQ after the noise reduction: cut rumble below 70 Hz, a little more body around
    /// 180 Hz (the heel thump), and soften the fizzy top above 7 kHz (the crunchy, maraca-like part).
    /// Plain filters like these don't flicker, unlike heavier noise reduction.</summary>
    public static float BodyDb = 3f, TopDb = -5f;
    static float[] Tone(float[] x, int rate)
    {
        x = Biquad(x, HighPass(70, 0.707, rate));
        x = Biquad(x, Peak(180, 1.0, BodyDb, rate));
        x = Biquad(x, HighShelf(7000, TopDb, rate));
        return x;
    }

    // RBJ audio EQ cookbook filters: (b0, b1, b2, a1, a2), normalised by a0.
    static double[] HighPass(double f, double q, int rate)
    {
        double w = 2 * Math.PI * f / rate, c = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al;
        return new[] { (1 + c) / 2 / a0, -(1 + c) / a0, (1 + c) / 2 / a0, -2 * c / a0, (1 - al) / a0 };
    }
    static double[] Peak(double f, double q, double db, int rate)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / rate, c = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al / A;
        return new[] { (1 + al * A) / a0, -2 * c / a0, (1 - al * A) / a0, -2 * c / a0, (1 - al / A) / a0 };
    }
    static double[] HighShelf(double f, double db, int rate)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / rate, c = Math.Cos(w), s = Math.Sin(w);
        double al = s / 2 * Math.Sqrt(2), sq = 2 * Math.Sqrt(A) * al;
        double a0 = (A + 1) - (A - 1) * c + sq;
        return new[] {
            A * ((A + 1) + (A - 1) * c + sq) / a0, -2 * A * ((A - 1) + (A + 1) * c) / a0, A * ((A + 1) + (A - 1) * c - sq) / a0,
            2 * ((A - 1) - (A + 1) * c) / a0, ((A + 1) - (A - 1) * c - sq) / a0 };
    }
    static float[] Biquad(float[] x, double[] k)
    {
        var y = new float[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = k[0] * x[i] + k[1] * x1 + k[2] * x2 - k[3] * y1 - k[4] * y2;
            x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = (float)v;
        }
        return y;
    }

    // ---------- cutting into single steps ----------

    /// <summary>Finds each footstep in the cleaned recording and writes it as its own short file.
    /// Returns the step start times (s) and the file paths.</summary>
    public static (List<float> onsets, List<string> files) SliceSteps(float minGap = 0.18f, float maxLength = 0.45f)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(CleanPath);
        int ch = clip.channels, rate = clip.frequency, n = clip.samples;
        var data = new float[n * ch];
        clip.GetData(data, 0);

        // Loudness envelope in 5 ms steps; a step starts where it jumps well above what came just before.
        int hop = rate / 200;
        var env = new List<double>();
        for (int i = 0; i + hop <= n; i += hop) env.Add(Rms(data, ch, i, i + hop));
        double peak = 0; foreach (var e in env) peak = Math.Max(peak, e);
        var onsets = new List<float>();
        float last = -10f;
        for (int k = 4; k < env.Count; k++)
        {
            double before = 0; for (int j = k - 4; j < k; j++) before = Math.Max(before, env[j]);
            double now = env[k];
            float t = k * hop / (float)rate;
            if (now > peak * 0.12 && now > before * 2.2 && t - last >= minGap) { onsets.Add(t); last = t; }
        }

        if (!AssetDatabase.IsValidFolder(StepFolder)) AssetDatabase.CreateFolder("Assets/SFX", "Footstep Steps");
        foreach (var old in Directory.GetFiles(StepFolder, "Step *.wav")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
        // Even out the steps: bring each to the median loudness (within 0.6x-1.5x, so nothing gets boosted into noise).
        var levels = new List<double>();
        for (int s = 0; s < onsets.Count; s++)
        {
            int a = (int)(onsets[s] * rate), b = Math.Min(n, a + rate / 10);
            levels.Add(Rms(data, ch, a, b));
        }
        var sorted = new List<double>(levels); sorted.Sort();
        double median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 1;
        var files = new List<string>();
        for (int s = 0; s < onsets.Count; s++)
        {
            int a = Math.Max(0, (int)(onsets[s] * rate) - rate / 200);                 // 5 ms before the hit
            int nextStart = s + 1 < onsets.Count ? (int)(onsets[s + 1] * rate) - rate / 200 : n;
            int b = Math.Min(Math.Min(nextStart, a + (int)(maxLength * rate)), n);
            int len = b - a;
            var piece = new float[len * ch];
            Array.Copy(data, a * ch, piece, 0, len * ch);
            int fadeIn = rate / 1000, fadeOut = Math.Min(len / 3, rate / 25);           // 1 ms in, up to 40 ms out
            float level = (float)Math.Clamp(levels[s] > 0 ? median / levels[s] : 1, 0.6, 1.5);
            for (int i = 0; i < len; i++)
            {
                float g = level;
                if (i < fadeIn) g *= i / (float)fadeIn;
                if (i >= len - fadeOut) g *= (len - 1 - i) / (float)fadeOut;
                for (int c = 0; c < ch; c++) piece[i * ch + c] *= g;
            }
            string path = $"{StepFolder}/Step {s + 1:00}.wav";
            WriteWav(path, piece, ch, rate);
            files.Add(path);
        }
        AssetDatabase.Refresh();
        return (onsets, files);
    }

    // ---------- helpers ----------

    static double Rms(float[] d, int ch, int from, int to)
    {
        double s = 0; int n = 0;
        for (int i = Math.Max(0, from); i < to && i * ch < d.Length; i++) { s += d[i * ch] * d[i * ch]; n++; }
        return n > 0 ? Math.Sqrt(s / n) : 0;
    }

    static double PeakWindow(float[] d, int ch, int rate)
    {
        double best = 0; int win = rate / 20, n = d.Length / ch;
        for (int i = 0; i + win <= n; i += win) best = Math.Max(best, Rms(d, ch, i, i + win));
        return best;
    }

    static double Db(double x) => 20 * Math.Log10(Math.Max(x, 1e-9));

    public static void WriteWav(string path, float[] samples, int channels, int rate)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int bytes = samples.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (var v in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
    }
}
