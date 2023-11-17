using CrowdControl.Games.Packs.MCCHaloCE.Effects;
using System;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE;

public partial class MCCHaloCE
{
    // Changes current ammo, clip and battery by the percentage of their current values. Negative values add ammo.
    public void TakeAwayAmmoFromCurrentWeapon(float percentage)
    {
        try
        {
            if (percentage < 0)
            {
                QueueOneShotEffect((short)OneShotEffect.Give, 0);
            }
            else
            {
                QueueOneShotEffect((short)OneShotEffect.Yoink, 0);
            }

            // Clip ammo
            if (TryGetIndirectByteArray(WeaponClipAmmoPointer_ch, 0, 2, out byte[] clipAmmoBytes))
            {
                short clipAmmo = BitConverter.ToInt16(clipAmmoBytes, 0);
                clipAmmo = (short)(clipAmmo * (1 - percentage));
                CcLog.Message("New clip ammo amount: " + clipAmmo);
                TrySetIndirectShort(clipAmmo, WeaponClipAmmoPointer_ch, 0, false);
            }

            int secondaryAmmoStoreRelativeOffset = -0x14;
            // Clip ammo (secondary address used on some weapons)
            if (TryGetIndirectByteArray(WeaponClipAmmoPointer_ch, secondaryAmmoStoreRelativeOffset, 2, out byte[] clipAmmoBytesSecondary))
            {
                short clipAmmo = BitConverter.ToInt16(clipAmmoBytesSecondary, 0);
                clipAmmo = (short)(clipAmmo * (1 - percentage));
                CcLog.Message("New clip ammo amount: " + clipAmmo);
                TrySetIndirectShort(clipAmmo, WeaponClipAmmoPointer_ch, secondaryAmmoStoreRelativeOffset, false);
            }

            int beltAmmoOffset = (-0x282 + 0x27e);
            if (TryGetIndirectByteArray(WeaponClipAmmoPointer_ch, beltAmmoOffset, 2, out byte[] beltAmmoBytes))
            {
                short beltAmmo = BitConverter.ToInt16(beltAmmoBytes, 0);
                beltAmmo = (short)(beltAmmo * (1 - percentage));
                CcLog.Message("New belt ammo amount: " + beltAmmo);
                TrySetIndirectShort(beltAmmo, WeaponClipAmmoPointer_ch, beltAmmoOffset, false);
            }

            if (TryGetIndirectByteArray(WeaponClipAmmoPointer_ch, beltAmmoOffset + secondaryAmmoStoreRelativeOffset, 2, out byte[] beltAmmoBytesSecondary))
            {
                short beltAmmo = BitConverter.ToInt16(beltAmmoBytesSecondary, 0);
                beltAmmo = (short)(beltAmmo * (1 - percentage));
                CcLog.Message("New belt ammo amount: " + beltAmmo);
                TrySetIndirectShort(beltAmmo, WeaponClipAmmoPointer_ch, beltAmmoOffset + secondaryAmmoStoreRelativeOffset, false);
            }

            // Plasma battery is defined by a property named "age". Age = 0 is full battery, age = 1 is depleted battery.
            int weaponAgeOffset = -0x86 + 0x4;
            if (TryGetIndirectByteArray(WeaponClipAmmoPointer_ch, weaponAgeOffset, 4, out byte[] ageBytes))
            {
                float age = BitConverter.ToSingle(ageBytes);
                float battery = 1 - age;
                battery *= (1 - percentage);
                age = 1 - battery;
                CcLog.Message("New battery amount: " + (1 - battery));
                TrySetIndirectFloat(age, WeaponClipAmmoPointer_ch, weaponAgeOffset, false);
            }
        }
        catch (Exception ex)
        {
            CcLog.Error(ex, "Something went wrong taking ammo away");
        }
    }
}