using NUnit.Framework;
using UnityEngine;

/// <summary>
/// The ear's error (Plan-Busqueda-Nemesis Fase 1, D39): bounded by the evidence radius, flat, and
/// smooth in time — the same noise heard on the next sweep comes from nearly the same wrong place.
/// </summary>
public class HearingLocalizationTests
{
    [Test]
    public void MaxError_IsAFractionOfTheRadius()
    {
        Assert.AreEqual(1.5f, HearingLocalization.MaxError(2.5f, 0.6f), 1e-5f);
        Assert.AreEqual(0f, HearingLocalization.MaxError(2.5f, 0f), 1e-5f);
        Assert.AreEqual(2.5f, HearingLocalization.MaxError(2.5f, 3f), 1e-5f, "clamped to the radius");
    }

    [Test]
    public void Offset_NeverLongerThanTheError_AndFlat()
    {
        for (int i = 0; i < 500; i++)
        {
            Vector3 offset = HearingLocalization.Offset(42f, i * 0.37f, 4f, 2f);
            Assert.LessOrEqual(offset.magnitude, 2f + 1e-4f);
            Assert.AreEqual(0f, offset.y);
        }
    }

    [Test]
    public void Offset_ZeroError_IsZero()
    {
        Assert.AreEqual(Vector3.zero, HearingLocalization.Offset(42f, 3f, 4f, 0f));
    }

    [Test]
    public void Offset_DriftsSmoothly()
    {
        // One sweep apart (0.1 s) with a 4 s drift: a small fraction of the error, never a jump.
        for (int i = 0; i < 200; i++)
        {
            float t = i * 0.73f;
            Vector3 a = HearingLocalization.Offset(7f, t, 4f, 2f);
            Vector3 b = HearingLocalization.Offset(7f, t + 0.1f, 4f, 2f);
            Assert.Less((a - b).magnitude, 0.35f, $"jumped at t={t:0.00}");
        }
    }

    [Test]
    public void Offset_ActuallyMisses()
    {
        // Over a long stretch the error is used, not stuck near zero.
        float longest = 0f;
        for (int i = 0; i < 200; i++)
            longest = Mathf.Max(longest, HearingLocalization.Offset(3f, i * 0.5f, 4f, 2f).magnitude);

        Assert.Greater(longest, 1f);
    }
}
