using System;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using MagitekMonsters.Models;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly TrainerProfiles trainerProfiles = new();
    private TrainerCardPreferences? trainerCardDraft;
    private bool trainerCardDirty;
    private bool trainerCardShared;
    private bool openTrainerProfileTab;
    private string trainerProfileId = string.Empty;
    private string trainerProfileError = string.Empty;
    private TrainerProfileDto? viewedTrainerProfile;

    private void ResetTrainerProfiles()
    {
        trainerProfiles.Clear();
        trainerCardOwner = string.Empty;
        trainerCardDraft = null;
        trainerCardDirty = false;
        trainerProfileId = trainerProfileError = string.Empty;
        viewedTrainerProfile = null;
        openTrainerProfileTab = false;
    }

    private void PrepareTrainerCard()
    {
        if (trainerCardOwner != trainer!.User.Id)
        {
            trainerCardOwner = trainer.User.Id;
            trainerCardSearch = string.Empty;
            trainerCardDraft = null;
            trainerCardDirty = false;
        }
        plugin.Configuration.TrainerCards ??= new();
        var saved = trainer.Collection.TrainerCard ?? TrainerCard.Preferences(plugin.Configuration.TrainerCards, trainer.User.Id);
        if (trainerCardDraft is null || !trainerCardDirty && !TrainerCard.Same(saved, trainerCardDraft)) trainerCardDraft = saved.Copy();
        trainerCardShared = trainerCardDraft.Shared;
    }

    private async Task PublishTrainerCard()
    {
        var saved = await InSession(plugin.Api.SaveTrainerCard(trainerCardDraft!.Copy()));
        trainer!.Collection.TrainerCard = saved;
        plugin.Configuration.TrainerCards[trainer.User.Id] = saved.Copy();
        plugin.Configuration.Save();
        trainerCardDraft = saved.Copy();
        trainerCardDirty = false;
        trainerProfiles.Invalidate(trainer.User.TrainerId);
        SetStatus(saved.Shared ? "Trainer card saved and shared." : "Trainer card saved. Your card is private.");
    }

    private async Task MigrateTrainerCard()
    {
        if (trainer?.Collection.TrainerCard is not null || trainer is null
            || plugin.Configuration.TrainerCards is null || !plugin.Configuration.TrainerCards.TryGetValue(trainer.User.Id, out var local)) return;
        try { trainer.Collection.TrainerCard = await InSession(plugin.Api.SaveTrainerCard(local.Copy(), onlyIfMissing: true)); }
        catch (StaleSessionException) { throw; }
        catch (ApiException exception) when (exception.StatusCode == 426) { throw; }
        catch (Exception exception) { Plugin.Log.Warning(exception, "Trainer card migration deferred"); }
    }

    private void OpenTrainerProfile(string id)
    {
        trainerProfileId = id;
        openTrainerProfileTab = true;
        mainSection = 14;
        Run(LoadTrainerProfile, "Loading trainer card...");
    }

    private async Task LoadTrainerProfile()
    {
        var id = trainerProfileId.Trim().ToUpperInvariant();
        viewedTrainerProfile = null;
        trainerProfileError = string.Empty;
        try
        {
            var card = await InSession(trainerProfiles.Get(id, () => plugin.Api.GetTrainerProfile(id, disposeCancellation.Token)));
            viewedTrainerProfile = card;
            SetStatus("Trainer card loaded.");
        }
        catch (ApiException exception) when (exception.StatusCode == 404)
        {
            trainerProfileError = "This trainer card is unavailable or private.";
            SetStatus(trainerProfileError);
        }
    }

    private void DrawTrainerProfileLookup()
    {
        ImGui.TextWrapped("Enter a Trainer ID to view a shared card.");
        ImGui.BeginDisabled(busy);
        ImGui.SetNextItemWidth(-1);
        var enter = ImGui.InputTextWithHint("##TrainerProfileId", "MM-00000001", ref trainerProfileId, 24, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(trainerProfileId));
        if (DrawAccentSmallButton("Open card", CompanionAccent) || enter) Run(LoadTrainerProfile, "Loading trainer card...");
        ImGui.SameLine();
        if (DrawAccentSmallButton("Refresh card", CompanionMuted))
        {
            trainerProfiles.Invalidate(trainerProfileId.Trim());
            Run(LoadTrainerProfile, "Refreshing trainer card...");
        }
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.Spacing();
        if (trainerProfileError.Length > 0) ImGui.TextWrapped(trainerProfileError);
        if (viewedTrainerProfile is not null) DrawTrainerProfilePreview(viewedTrainerProfile);
    }
}
