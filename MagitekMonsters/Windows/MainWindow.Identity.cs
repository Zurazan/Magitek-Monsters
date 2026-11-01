using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Contracts;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private string identityDraftOwner = string.Empty;
    private string vanityDraft = string.Empty;
    private string uidDraft = string.Empty;
    private string uidPromptOwner = string.Empty;
    private bool requestUidPrompt;
    private bool uidConfirmed;

    private void DrawTrainerIdentity()
    {
        if (trainer is null) return;
        if (identityDraftOwner != trainer.User.Id)
        {
            identityDraftOwner = trainer.User.Id;
            vanityDraft = trainer.User.VanityCode ?? string.Empty;
        }
        ImGui.TextUnformatted("PERMANENT UID");
        DrawCopyableTrainerId(trainer.User.TrainerId, ImGui.GetContentRegionAvail().X);
        if (trainer.User.CanChooseTrainerUid)
        {
            ImGui.TextWrapped("You can choose your permanent UID once. Your Pokémon and progress stay with your account.");
            if (DrawAccentSmallButton("Choose permanent UID", CompanionAccent)) requestUidPrompt = true;
        }
        else ImGui.TextWrapped("Your permanent UID is fixed.");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextUnformatted("VANITY CODE");
        ImGui.TextWrapped("A free, optional alias for invitations and trainer lookups. You can change or remove it whenever you like.");
        ImGui.SetNextItemWidth(Math.Min(300 * CompanionScale, ImGui.GetContentRegionAvail().X));
        ImGui.InputText("##VanityCode", ref vanityDraft, 15);
        var valid = TrainerUid.TryVanity(vanityDraft, out var code);
        ImGui.TextWrapped("Use 3-15 letters, numbers, underscores or hyphens. Codes are unique regardless of capitals.");
        var actionStart = ImGui.GetCursorPosX();
        var actionWidth = ImGui.GetContentRegionAvail().X;
        ImGui.BeginDisabled(busy || !valid || code == trainer.User.VanityCode);
        if (DrawAccentSmallButton("Save vanity code", CompanionAccent)) Run(() => SaveIdentity(() => plugin.Api.SaveVanity(code)), "Saving vanity code...");
        ImGui.EndDisabled();
        if (trainer.User.VanityCode is not null)
        {
            if (ImGui.GetItemRectSize().X + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2 <= actionWidth) ImGui.SameLine();
            else ImGui.SetCursorPosX(actionStart);
            ImGui.BeginDisabled(busy);
            if (DrawAccentSmallButton("Remove", CompanionMuted)) Run(() => SaveIdentity(() => plugin.Api.SaveVanity(null)), "Removing vanity code...");
            ImGui.EndDisabled();
        }
    }

    private void DrawUidChoicePrompt()
    {
        if (trainer is null) return;
        const string title = "Choose your permanent UID";
        var automatic = trainer.User.CanChooseTrainerUid && uidPromptOwner != trainer.User.Id && trainer.StarterChosen && !busy
            && plugin.Connection.State == ServerConnectionState.Connected && battle is null && pvpStatus?.Battle is null && raidRoom is null;
        if ((requestUidPrompt || automatic) && !ImGui.IsPopupOpen(title) && !ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopupId))
        {
            requestUidPrompt = false;
            uidPromptOwner = trainer.User.Id;
            uidDraft = trainer.User.TrainerId;
            uidConfirmed = false;
            ImGui.OpenPopup(title);
        }
        if (!BeginConfirmationModal(title)) return;
        if (!trainer.User.CanChooseTrainerUid || uidPromptOwner != trainer.User.Id) ImGui.CloseCurrentPopup();
        else
        {
            ImGui.TextWrapped("Your account has a new UID. Keep it or choose your own once. This choice is permanent.");
            ImGui.TextColored(CompanionAccent, $"Generated UID: {trainer.User.TrainerId}");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##PermanentUid", ref uidDraft, 10);
            ImGui.TextWrapped("Exactly 10 letters or numbers. I, O, 0 and 1 are excluded. Other trainers' UIDs and vanity codes cannot be used.");
            var valid = TrainerUid.TryNormalize(uidDraft, out var code);
            ImGui.Checkbox("##UidConfirmed", ref uidConfirmed);
            ImGui.SameLine(); ImGui.TextWrapped("I understand this UID cannot be changed again.");
            ImGui.BeginDisabled(busy || !uidConfirmed);
            if (DrawAccentButton("Keep generated UID", new Vector2(-1, 34 * CompanionScale), CompanionMuted))
            {
                ImGui.CloseCurrentPopup();
                Run(() => SaveIdentity(() => plugin.Api.ChooseTrainerUid(new(null, true, true))), "Confirming permanent UID...");
            }
            ImGui.EndDisabled();
            if (DrawConfirmationActions("Confirm chosen UID", CompanionAccent, busy || !uidConfirmed || !valid, "Decide later"))
            {
                ImGui.CloseCurrentPopup();
                Run(() => SaveIdentity(() => plugin.Api.ChooseTrainerUid(new(code, false, true))), "Confirming permanent UID...");
            }
            ImGui.TextDisabled("You can return here from Trainer Card > Identity.");
        }
        EndConfirmationModal();
    }

    private async Task SaveIdentity(Func<Task<System.Text.Json.JsonElement>> save)
    {
        await InSession(save());
        try { trainer = await InSession(plugin.Api.GetMe()); }
        catch (StaleSessionException) { throw; }
        catch (Exception) { throw new ApiException(409, "Your code was saved. Refresh your trainer data to see it.", "mutation_committed"); }
        identityDraftOwner = string.Empty;
        trainerProfiles.Clear();
        SetStatus("Trainer identity saved.");
    }
}
