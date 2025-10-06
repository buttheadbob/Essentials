using System;

namespace Essentials;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ConditionAttribute(string command, string? invertedCommand = null, string? helpText = null) : Attribute
{
    public string Command => command;
    public string? InvertCommand => invertedCommand;
    public string? HelpText => helpText;
    
}