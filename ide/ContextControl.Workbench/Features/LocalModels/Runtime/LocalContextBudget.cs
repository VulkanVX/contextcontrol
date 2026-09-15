namespace ContextControl.Workbench.Services;

public sealed record LocalContextDecision(int Tokens, int EstimatedInput, int OutputReserve, bool EnoughRoom, string Detail);

/// <summary>Chooses request size, without claiming a tokenizer-exact count or measured stability.</summary>
public static class LocalContextBudget
{
    public static LocalContextDecision Choose(LocalResourceSettings settings, LocalModelMemory memory,
        LocalLlmHardwareProfile hardware, int manualContext, string prompt, bool thinking, bool game,
        bool canAdapt, int? serverLimit = null)
    {
        settings = settings.Normalize();
        var input = (int)Math.Ceiling(prompt.Length / 3d) + 256;
        var reserve = (game ? 8192 : 2048) + (thinking ? 4096 : 0);
        var target = settings.ContextCeiling;
        if (settings.ContextMode == "Adaptive")
        {
            var demand = (long)input + reserve;
            var bucket = 4096;
            while (bucket < demand && bucket < 1048576) bucket *= 2;
            target = Math.Min(target, bucket);
        }
        var context = settings.Enabled && settings.AutoContext ? target : manualContext;
        if (canAdapt && settings.Enabled && settings.AutoContext)
            context = LocalResourcePlanner.Plan(memory, hardware, settings.ContextMode == "Maximum fit" ? settings
                : settings with { ContextMode = "Custom", MaxContextTokens = target }).ContextTokens;
        if (memory.MaxContext is > 0) context = Math.Min(context, memory.MaxContext.Value);
        if (serverLimit is > 0) context = Math.Min(context, serverLimit.Value);
        context = Math.Clamp(context, 1, 1048576);
        var room = input + 1024 <= context;
        var mode = settings.Enabled && settings.AutoContext ? settings.ContextMode : "Manual";
        var detail = $"{mode} · {context:N0} context · ~{input:N0} input · {Math.Max(0, context - input):N0} tokens left for thinking + output.";
        if (input + reserve > context) detail += " Less room than the preferred output reserve; shorten input or increase the mode/ceiling.";
        if (serverLimit is > 0) detail += " Server allocation is fixed until its model is reloaded.";
        return new(context, input, reserve, room, detail);
    }
}
