using UnityEngine;

using Obsi.Doc;

[ObsidocIncludePrivate]
[Obsidoc("Interface used as an exemple","Test","Exemple")]
public abstract class ObsidocInterfaceExemple
{
    public int lama;
    public string alpaca;
    private bool lamas;
    private bool Teest { get; }
    public abstract bool IsTest { get; }
    public abstract void Test();
    public abstract void Test2();
}