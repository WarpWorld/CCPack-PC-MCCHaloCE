using ConnectorLib.Inject.AddressChaining;
using CrowdControl.Games.Packs.MCCHaloCE.Utilites.ByteArrayBuilding;
using System;
using System.Linq;
using CcLog = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs.MCCHaloCE
{
    public partial class MCCHaloCE
    {
        private const long ConditionalDamageInjection_ShieldsOffset = 0xba0475;
        private const long ConditionalDamageInjection_HealthOffset = 0xb9fdf3;

        private const string OnDamageConditionalId = "ondamageconditional";

        private bool ShouldInjectDamageFactors
        { get { return PlayerReceivedDamageFactor != 1 || OthersReceivedDamageFactor != 1 || InstakillEnemies; } }

        /// <summary>
        /// Injects code that applies a factor to the damage received by player and other units.
        /// </summary>
        /// <param name="playerFactor">Factor by which the damage received by the player is multiplied.</param>
        /// <param name="othersFactor">Factor by which the damage received by the other units is multiplied.</param>
        /// <param name="instakillEnemies">If true, enemies receive a massive flat amount of damage when hit.</param>
        private bool InjectConditionalDamageMultiplier()
        {
            UndoInjection(OnDamageConditionalId);

            float playerFactor = PlayerReceivedDamageFactor;
            float othersFactor = OthersReceivedDamageFactor;
            bool instakillEnemies = InstakillEnemies;

            // Shields
            // Replaced bytes:
            //halo1.dll + BA0475 - F3 0F5C CA            -subss xmm1,xmm2
            //halo1.dll + BA0479 - F3 41 0F11 0E - movss[r14],xmm1
            //halo1.dll + BA047E - 41 F6 45 00 02 - test byte ptr[r13 + 00],02 { 2 }
            CcLog.Message("Injecting shields factor");
            InjectSpecificConditionalDamageMultiplier(ConditionalDamageInjection_ShieldsOffset, 0x0E,
                0x15, // xmm2
                new byte[] { 0x49, 0x8B, 0xCE }, // mov rcx, r14,
                    0x9a3 - 0xa0,                //a0: shields. 0x9a3: distance to the player discriminator
                    playerFactor, othersFactor,
                    instakillEnemies);

            // Health.
            // Replaced bytes:
            //halo1.dll + B9FDF3 - F3 0F10 83 9C000000 - movss xmm0,[rbx+0000009C]
            //halo1.dll + B9FDFB - F3 0F5C C6            -subss xmm0,xmm6
            //halo1.dll + B9FDFF - F3 0F11 83 9C000000 - movss[rbx + 0000009C],xmm0
            CcLog.Message("Injecting health factor");
            InjectSpecificConditionalDamageMultiplier(ConditionalDamageInjection_HealthOffset, 0x14,
                0x35, // xmm6
                new byte[] { 0x48, 0x8B, 0xCB },// mov rcx, rbx
                0x9a3, playerFactor, othersFactor,
                instakillEnemies);

            return true;
        }

        /// <summary>
        /// Performs the actual injection for <see cref="InjectConditionalDamageMultiplier(float, float, bool)"/>
        /// </summary>
        /// <param name="instructionOffset">Offset of the instruction where the code will be injected.</param>
        /// <param name="bytesToReplaceLength">How many bytes will be replaced at the injection point. Used to avoid breaking the next instructions.</param>
        /// <param name="damageRegister">Byte representing which register contains the damage value.</param>
        /// <param name="movPlayerPointingRegisterToRcxInstruction">
        ///     Instruction that takes the pointer to the player structure from a register,
        ///     and stores in rcx for our code to use. This pointer doesn't always point to the start of the structure, hence why we need
        ///     <paramref name="unitTypeDiscriminatorOffset"></paramref>.
        /// </param>
        /// <param name="unitTypeDiscriminatorOffset">
        ///     The offset in memory between the value used to determine if the unit is the player, and the pointer obtained
        ///     by the injected code.
        /// </param>
        /// <param name="playerFactor">See <see cref="InjectConditionalDamageMultiplier"/>.</param>
        /// <param name="othersFactor">See <see cref="InjectConditionalDamageMultiplier"/>.</param>
        /// <param name="instakillEnemies">See <see cref="InjectConditionalDamageMultiplier"/>.</param>
        private void InjectSpecificConditionalDamageMultiplier(long instructionOffset,
            int bytesToReplaceLength,
            byte damageRegister,
            byte[] movPlayerPointingRegisterToRcxInstruction,
            int unitTypeDiscriminatorOffset,
            float playerFactor,
            float othersFactor,
            bool instakillEnemies)
        {
            int playerDiscriminator = 0x3f; // 63
            int caveDataOffset = 0x130; // Address offset where the data will be stored in the new cave, with respect to its start.
            float ludicrousDamage = 0x7fffffffffffffff;

            // this is the previous instruction, so we can inject the jump without having to avoid overwriting a Jcc.
            var onDamageHealthSubstractionInstr_ch = AddressChain.Absolute(Connector, halo1BaseAddress + instructionOffset);

            IntPtr unitStructurePointerPointer = CreateCodeCave(ProcessName, 8); // todo: change the offset to point to the structure start.
            CreatedCaves.Add((OnDamageConditionalId, (long)unitStructurePointerPointer, 8));

            (long injectionAddress, byte[] originalBytes) = GetOriginalBytes(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength);
            ReplacedBytes.Add((OnDamageConditionalId, injectionAddress, originalBytes));

            // Checks if the current unit is the player or not, and jumps accordingly to apply the corresponding multiplication
            // (or replacing the damage value for instakills)
            byte[] prependedBytes = new byte[] {
                0x51 }// push rcx
            .Append(
                movPlayerPointingRegisterToRcxInstruction)
            .Append(
                0x48, 0x81, 0xc1).AppendNum(unitTypeDiscriminatorOffset) // mov rcx, <discriminator offset>
            .Append(
                0x81, 0x39).AppendNum(playerDiscriminator) // cmp [rcx], <discriminator value>
            .Append(
                0x59, // pop rcx
                0x75).AppendRelativePointer("enemyReceivedDamageMod", 0x0A) // jne to the original code start
            .Append(
                0xf3, 0x0f, 0x59, damageRegister).AppendNum(caveDataOffset - 28) // mulss xmm6, [hardcoded factor in program code]. xmm6 is the damage received here.
            .Append(
                0xEB).AppendRelativePointer("originalCode", 0x08 // Jmp to jump back to game code, so only the player received damage is updated.
            )
            .LocalJumpLocation("enemyReceivedDamageMod")
            .Append(instakillEnemies
            ?
                new byte[] { 0xF3, 0x0f, 0x10, damageRegister }.AppendNum(caveDataOffset - 38 + 8) // movss xmm6 the ludicrous damage value
            :
                new byte[] { 0xf3, 0x0f, 0x59, damageRegister }.AppendNum(caveDataOffset - 38 + 4) // mulss xmm6 by the second factor
            );

            byte[] caveBytes = prependedBytes.Concat(originalBytes).Concat(GenerateJumpBytes(injectionAddress + bytesToReplaceLength)).ToArray();
            CcLog.Message("Injection address: " + injectionAddress.ToString("X"));

            long cavePointer = CodeCaveInjection(onDamageHealthSubstractionInstr_ch, bytesToReplaceLength, caveBytes);
            CreatedCaves.Add((OnDamageConditionalId, cavePointer, StandardCaveSizeBytes));

            // Set the in place data
            AddressChain dataPointer = AddressChain.Absolute(Connector, cavePointer + caveDataOffset);
            dataPointer.Offset(0).SetFloat(playerFactor);
            dataPointer.Offset(4).SetFloat(othersFactor);
            dataPointer.Offset(8).SetFloat(ludicrousDamage);
        }
    }
}