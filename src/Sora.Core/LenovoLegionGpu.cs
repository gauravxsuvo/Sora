namespace Sora.Core;

public enum LegionGpuMode { Hybrid, IntegratedOnly, Auto, Discrete }
public sealed record LegionGpuState(LegionGpuMode ConfiguredMode, LegionGpuMode[] AvailableModes);
public interface ILegionGpuTransport
{
    LegionGpuState ReadGpu();
    void WriteGpu(LegionGpuMode mode);
}
