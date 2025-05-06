using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ExtendedHotbars
{
	class HotkeysPatches
	{
		private static Color32 _greyOut = new Color32(255, 255, 255, 50);
		public static void HotkeyStart_postPatch(Hotkeys __instance)
		{
			if (__instance.AssignedSpell != null || __instance.AssignedSkill != null || __instance.AssignedItem != null)
				__instance.MyImage.enabled = true;
		}


		//Basically copies the original function, we're just checking for our key + no gamepad support, sorry.
		public static void UpdateHotkey(Hotkeys _hk, int barID, int hkkIdx)
		{
			var hk = ExtendedHotbarsPlugin.barHotkeys[barID][hkkIdx];
			if (hk.Value.IsDown() && !GameData.PlayerTyping && !GameData.AuctionWindowOpen)
			{
				ExtendedHotbarsPlugin.isOurHotkey = true;
				ExtendedHotbarsPlugin.doHotkeyTaskMethod.Invoke(_hk, null);
				ExtendedHotbarsPlugin.isOurHotkey = false;
			}

			var hkTxt = ExtendedHotbarsPlugin.myHKText.GetValue(_hk) as Text;
			var svdStr = ExtendedHotbarsPlugin.savedStr.GetValue(_hk) as String;

			if (_hk.Cooldown > 0f)
			{
				_hk.Cooldown -= 60f * Time.deltaTime;
			}

			if (_hk.Cooldown > 0f && (_hk.AssignedSkill != null || _hk.AssignedSpell != null || _hk.AssignedItem != null))
			{
				hkTxt.text = Mathf.RoundToInt(_hk.Cooldown / 60f).ToString();
				if (_hk.MyImage.color != _greyOut)
				{
					_hk.MyImage.color = _greyOut;
				}
			}
			else if (hkTxt.text != svdStr)
			{
				hkTxt.text = svdStr;
			}

			if (_hk.Cooldown <= 0f && _hk.MyImage.color != Color.white)
			{
				_hk.MyImage.color = Color.white;
			}

			HotkeyUpdate_postPatch(_hk);
		}

		public static bool HotkeyUpdate_prePatch(Hotkeys __instance)
		{
			foreach (var bar in ExtendedHotbarsPlugin.additionalHotbars)
			{
				int hkkIdx = 0;
				foreach (var hkk in bar.Value.hotkeys)
				{
					if (__instance == hkk.Value)
					{
						UpdateHotkey(__instance, bar.Key, hkkIdx);
						return false;
					}
					hkkIdx++;
				}
			}
			return true;
		}

		public static void HotkeyUpdate_postPatch(Hotkeys __instance)
		{
			string hkSID = ExtendedHotbarsPlugin.getHKSkillId(__instance);

			if (GameData.CurrentCharacterSlot == null) return;
			if (!ExtendedHotbarsPlugin.HasFlag) return;

			if (!ExtendedHotbarsPlugin.CurrentCharData.hotkeyCooldowns.ContainsKey(hkSID))
				ExtendedHotbarsPlugin.CurrentCharData.hotkeyCooldowns.Add(hkSID, 0);

			var cooldown = __instance.Cooldown;
			//Do we still need this?
			ExtendedHotbarsPlugin.CurrentCharData.hotkeyCooldowns[hkSID] = cooldown;
			//Need to update all other hotbars cd if they have that skill, joy

			
		}


		//we need to prevent alt and ctrl (or really anything) from causing skills to activate
		public static bool DoHotkeyTask_prePatch(Hotkeys __instance)
		{
			if (ExtendedHotbarsPlugin.isOurHotkey)
			{
				DoHotkeyTask_Custom(__instance);
				return false;
			}

			var modifiers = ExtendedHotbarsPlugin.ExtendBarHotkey.Value.Modifiers;
			modifiers = modifiers.Concat(ExtendedHotbarsPlugin.BarPlusHotkey.Value.Modifiers);
			modifiers = modifiers.Concat(ExtendedHotbarsPlugin.BarMinusHotkey.Value.Modifiers);
			foreach (var vv in ExtendedHotbarsPlugin.barHotkeys)
			{
				foreach (var hkk in vv.Value)
					modifiers = modifiers.Concat(hkk.Value.Modifiers);
			}

			bool isPressingMod = false;
			foreach (var mod in modifiers)
			{
				if (Input.GetKey(mod))
				{
					isPressingMod = true;
					break;
				}
			}

			if (!isPressingMod)
				DoHotkeyTask_Custom(__instance);
			return false;
		}

		public static void EndSpell_prePatch(SpellVessel __instance)
		{
			var SpellSource = ExtendedHotbarsPlugin.spellSource.GetValue(__instance) as CastSpell;
			if (SpellSource.isPlayer)
			{
				if (__instance.spell.AutomateAttack && GameData.GM.AutoEngageAttackOnSkill)
				{
					GameData.PlayerCombat.ForceAttackOn();
				}

				var hkSID = __instance.spell.Id;
				foreach (var bar in ExtendedHotbarsPlugin.additionalHotbars)
				{
					foreach (var _hk in bar.Value.hotkeys.Values)
					{
						string _hkSID = ExtendedHotbarsPlugin.getHKSkillId(_hk);
						if (_hkSID == hkSID)
						{
							_hk.Cooldown = __instance.spell.Cooldown * 60f;
							if (SpellSource.MyChar.MySkills != null)
							{
								_hk.Cooldown -= __instance.spell.Cooldown * 60f * ((float)SpellSource.MyChar.MySkills.GetAscensionRank("7758218") * 0.1f);
							}
						}
						else if (_hk.Cooldown < 2f)
						{
							_hk.Cooldown = 20f;
						}
					}
				}
			}
		}
		public static void EndSpellNoCD_prePatch(SpellVessel __instance)
		{
			var SpellSource = ExtendedHotbarsPlugin.spellSource.GetValue(__instance) as CastSpell;
			if (SpellSource.isPlayer)
			{
				var hkSID = __instance.spell.Id;
				foreach (var bar in ExtendedHotbarsPlugin.additionalHotbars)
				{
					foreach (var _hk in bar.Value.hotkeys.Values)
					{
						string _hkSID = ExtendedHotbarsPlugin.getHKSkillId(_hk);
						if (_hkSID == hkSID)
						{
							_hk.Cooldown = 20f;
						}
						else if (_hk.Cooldown < 2f)
						{
							_hk.Cooldown = 20f;
						}
					}
				}
			}
		}

		//this sucks but what can you do, the proper way would probably be to do some IL magic to remove those loops over all the hotkeys, because we're doing that later anyway
		public static void DoHotkeyTask_Custom(Hotkeys __instance)
		{
			var hk = __instance;
			string hkSID = ExtendedHotbarsPlugin.getHKSkillId(__instance);
			ExtendedHotbarsPlugin._logger.LogInfo($"CustomHotkeyTask {hk.Cooldown}");
			if (hk.AssignedSpell != null && hk.thisHK == Hotkeys.HKType.Spell && hk.Cooldown <= 0f)
			{
				if (GameData.PlayerControl.GetComponent<CastSpell>().KnownSpells.Contains(hk.AssignedSpell))
				{
					bool flag = false;
					if (GameData.PlayerControl.CurrentTarget != null && !hk.AssignedSpell.SelfOnly && hk.AssignedSpell.Type != Spell.SpellType.Misc)
					{
						flag = hk.PlayerSpells.StartSpell(hk.AssignedSpell, GameData.PlayerControl.CurrentTarget.MyStats);
					}
					else if (hk.AssignedSpell.SelfOnly && hk.AssignedSpell.Type != Spell.SpellType.Misc)
					{
						Character character = ((!(GameData.PlayerControl.CurrentTarget == null)) ? GameData.PlayerControl.CurrentTarget : GameData.PlayerControl.Myself);
						if (character.MyFaction != 0)
						{
							character = GameData.PlayerControl.Myself;
							flag = hk.PlayerSpells.StartSpell(hk.AssignedSpell, character.MyStats);
						}
						else
						{
							flag = hk.PlayerSpells.StartSpell(hk.AssignedSpell, character.MyStats);
						}


						if (flag)
						{
							hk.Cooldown = hk.AssignedSpell.Cooldown * 60f;
						}
						else if (hk.Cooldown < 2f)
						{
							hk.Cooldown = 2f;
						}

					}
					else if (hk.AssignedSpell.Type == Spell.SpellType.Misc)
					{
						flag = hk.PlayerSpells.StartSpell(hk.AssignedSpell, null);

						if (flag)
						{
							hk.Cooldown = hk.AssignedSpell.Cooldown * 60f;
						}
						else if (hk.Cooldown < 2f)
						{
							hk.Cooldown = 2f;
						}
					}
					else
					{
						UpdateSocialLog.CombatLogAdd("You must select a target for this spell!", "lightblue");
					}
				}
				else
				{
					UpdateSocialLog.LogAdd("This spell has been removed from the game.", "yellow");
				}
			}

			if (hk.AssignedSkill != null && hk.thisHK == Hotkeys.HKType.Skill && hk.Cooldown <= 0f)
			{
				bool flag2 = false;
				if (hk.AssignedSkill.SkillName != "Fishing")
				{
					flag2 = hk.PlayerSkills.DoSkill(hk.AssignedSkill, GameData.PlayerControl.CurrentTarget);
				}
				else
				{
					hk.PlayerSkills.MyFishing.StartFishing();
				}

				if (flag2)
				{
					hk.Cooldown = hk.AssignedSkill.Cooldown * 60f;
				}
				else if (hk.Cooldown < 2f)
				{
					hk.Cooldown = 2f;
				}

				if (flag2)
				{
					hk.Cooldown = hk.AssignedSkill.Cooldown;
				}
				else
				{
					hk.Cooldown = 2f;
				}
			}

			if (!(hk.AssignedItem != null) || hk.thisHK != Hotkeys.HKType.Item || !(hk.Cooldown <= 0f))
			{
				return;
			}

			if (hk.AssignedItem.MyItem.ItemEffectOnClick != null)
			{
				hk.AssignedItem.UseConsumable();
				if (hk.AssignedItem.Quantity <= 0 || hk.AssignedItem.MyItem == GameData.PlayerInv.Empty)
				{
					hk.ClearMe();
				}
			}

			if (hk.AssignedItem != null && hk.AssignedItem.MyItem.ItemSkillUse != null)
			{
				hk.AssignedItem.UseSkill();
			}


			//First update the default bar
			foreach (var _hk in GameData.GM.HKManager.AllHotkeys)
			{
				string _hkSID = ExtendedHotbarsPlugin.getHKSkillId(_hk);
				if (_hk != __instance && _hkSID == hkSID)
					_hk.Cooldown = hk.Cooldown;
			}
			//Now update our custom bars
			foreach (var bar in ExtendedHotbarsPlugin.additionalHotbars)
			{
				foreach (var _hk in bar.Value.hotkeys.Values)
				{
					string _hkSID = ExtendedHotbarsPlugin.getHKSkillId(_hk);
					if (_hk != __instance && _hkSID == hkSID)
						_hk.Cooldown = hk.Cooldown;
				}
			}
		}

		public static bool CanContinue()
		{
			if (GameData.InCharSelect) return false;
			if (ExtendedHotbarsPlugin.isDoingLoad) return false;
			if (!ExtendedHotbarsPlugin.isLoaded) return false;
			//we dont even have a bar yet?
			if (!ExtendedHotbarsPlugin.HasFlag) return false;

			return true;
		}

		public static (List<hotkeyItem>, int) GetBar(Hotkeys hk)
		{
			int barID = ExtendedHotbarsPlugin.CurrentBarID;
			int hkID = -1;

			foreach (var bar in ExtendedHotbarsPlugin.additionalHotbars)
			{
				int hkkIdx = 0;
				foreach (var hkk in bar.Value.hotkeys)
				{
					if (hk == hkk.Value)
					{
						hkID = hkkIdx;
						barID = bar.Key;
						break;
					}
					hkkIdx++;
				}
			}

			List<hotkeyItem> usingBar = null;
			if (ExtendedHotbarsPlugin.saveData.charFlags[ExtendedHotbarsPlugin.CurrentChar].hotkeyList.ContainsKey(barID))
				usingBar = ExtendedHotbarsPlugin.saveData.charFlags[ExtendedHotbarsPlugin.CurrentChar].hotkeyList[barID];

			if(hkID == -1)
			{
				//we need to find the correct index, because the game hotbar isn't in our additionalHotbars Dict
				int idx = 0;
				foreach (var _hk in GameData.GM.HKManager.AllHotkeys)
				{
					if(_hk == hk)
					{
						hkID = idx;
						break;
					}
					++idx;
				}
			}

			return (usingBar, hkID);
		}

		public static void AssignSpell_postPatch(Hotkeys __instance)
		{
			if (!CanContinue()) return;

			(var bar, int hkID) = GetBar(__instance);
			if (bar == null) return;

			bool valItem = __instance.AssignedSpell != null || __instance.AssignedSkill != null || __instance.AssignedItem != null;

			var _item = new hotkeyItem
			{
				type = valItem?(int)__instance.thisHK:9999,
				name = __instance.thisHK == Hotkeys.HKType.Spell ? __instance.AssignedSpell?.Id : __instance.thisHK == Hotkeys.HKType.Skill ? __instance.AssignedSkill?.Id : "",
				index = hkID,
				item = __instance.thisHK == Hotkeys.HKType.Item ? __instance.InvSlotIndex : -1
			};

			bar[hkID] = _item;
			ExtendedHotbarsPlugin.Save();
		}
	}
}
