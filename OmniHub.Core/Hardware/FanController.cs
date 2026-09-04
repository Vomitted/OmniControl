namespace OmniHub.Core.Hardware;

public sealed class FanController
{
    private readonly BiosInterop _bios;

    public FanController(BiosInterop bios) => _bios = bios;

    public byte GetFanCount() =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.GetFanCount, null, 4)[0];

    /// <summary>Per-fan type, one nibble/byte per fan slot.</summary>
    public byte[] GetFanType() =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.GetFanType, null, 128);

    /// <summary>Current fan speed level per fan (raw BIOS units, not RPM).</summary>
    public byte[] GetFanLevel() =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.GetFanLevel, null, 128);

    /// <summary>
    /// Directly commands fan 1 and fan 2 levels. Bypasses BIOS auto-control. The raw byte
    /// is NOT a 0-255 PWM duty cycle -- it's an RPM/100 target, usable range ~20-55 on
    /// this hardware family (see FanService.PercentToRaw for the sourcing). Passing a raw
    /// 0-255-scale value here would be wrong on both ends: too low to move the fan at all,
    /// or (well above the real ceiling) potentially treated as the "release to BIOS"
    /// sentinel some values in that range map to.
    /// </summary>
    public void SetFanLevel(byte fan1, byte fan2) =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.SetFanLevel, new byte[] { fan1, fan2, 0, 0 }, 4);

    /// <summary>Switches BIOS fan operating mode (Default/Performance/Cool/etc).</summary>
    public void SetFanMode(FanMode mode) =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.SetFanMode, new byte[] { 0xFF, (byte)mode, 0, 0 }, 4);

    /// <summary>
    /// Same BIOS command as <see cref="SetFanMode"/>, sent with OmniControlSuite's payload
    /// encoding instead of ours: a small 0/1/2 policy byte, and byte[2] set to 0x01 rather
    /// than 0x00. See <see cref="HpThermalPolicy"/> for why both encodings exist.
    ///
    /// Kept as a separate method rather than silently replacing SetFanMode: which encoding
    /// this machine actually honours is still an open question, and quietly switching the
    /// one the fan-curve loop depends on would be changing cooling behaviour on a guess.
    /// Send() throws on a non-zero BIOS return code, so a rejected policy surfaces as an
    /// exception rather than as a command that appeared to work.
    /// </summary>
    public void SetThermalPolicy(HpThermalPolicy policy) =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.SetFanMode, new byte[] { 0xFF, (byte)policy, 0x01, 0x00 }, 4);

    public byte[] GetFanTable() =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.GetFanTable, null, 128);

    public void SetFanTable(byte[] table128) =>
        _bios.Send(BiosCmdGroup.Default, FanCmd.SetFanTable, table128, 4);

    /// <summary>
    /// Hands control back to the BIOS automatic fan management.
    /// Always call this on clean shutdown -- never exit while fans are pinned
    /// to a manual level, or they can stay stuck at that level.
    /// </summary>
    public void RestoreAutomaticControl() => SetFanMode(FanMode.Default);
}
