namespace Lordsio.MusicStudio;

/// <summary>
/// Freeverb (Jezar at Dreampoint, public domain): eight damped comb filters and four all-pass filters per channel,
/// the right channel's delays a little longer, for a wide, smooth room.
/// </summary>
public sealed class Reverb
{
    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    private static readonly int[] AllPassTuning = { 556, 441, 341, 225 };
    private const int StereoSpread = 23;
    private const float InputGain = 0.015f;

    private readonly Comb[] _combL, _combR;
    private readonly AllPass[] _passL, _passR;

    public Reverb(int rate, float roomSize, float damping)
    {
        double scale = rate / 44100.0;
        float feedback = roomSize * 0.28f + 0.7f;
        _combL = CombTuning.Select(t => new Comb((int)(t * scale), feedback, damping)).ToArray();
        _combR = CombTuning.Select(t => new Comb((int)((t + StereoSpread) * scale), feedback, damping)).ToArray();
        _passL = AllPassTuning.Select(t => new AllPass((int)(t * scale))).ToArray();
        _passR = AllPassTuning.Select(t => new AllPass((int)((t + StereoSpread) * scale))).ToArray();
    }

    /// <summary>The wet signal for one stereo frame.</summary>
    public (float Left, float Right) Process(float left, float right)
    {
        float input = (left + right) * InputGain;
        float l = 0f, r = 0f;
        for (int i = 0; i < _combL.Length; i++)
        {
            l += _combL[i].Process(input);
            r += _combR[i].Process(input);
        }
        for (int i = 0; i < _passL.Length; i++)
        {
            l = _passL[i].Process(l);
            r = _passR[i].Process(r);
        }
        return (l, r);
    }

    private sealed class Comb(int size, float feedback, float damping)
    {
        private readonly float[] _buffer = new float[size];
        private int _index;
        private float _store;

        public float Process(float input)
        {
            float output = _buffer[_index];
            _store = output * (1f - damping) + _store * damping;
            _buffer[_index] = input + _store * feedback;
            _index = (_index + 1) % _buffer.Length;
            return output;
        }
    }

    private sealed class AllPass(int size)
    {
        private readonly float[] _buffer = new float[size];
        private int _index;

        public float Process(float input)
        {
            float buffered = _buffer[_index];
            _buffer[_index] = input + buffered * 0.5f;
            _index = (_index + 1) % _buffer.Length;
            return buffered - input;
        }
    }
}
