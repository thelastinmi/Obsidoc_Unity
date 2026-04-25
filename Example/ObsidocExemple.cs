using System.Collections.Generic;
using UnityEngine;
using Obsi.Doc;

[ObsidocIncludePrivate]
[Obsidoc("Script used as an exemple","Test","Exemple")]
public class ObsidocExemple : MonoBehaviour
{
    public int lama;
    public string alpaca;
    public List<int> lamas = new List<int>();
    public List<string> alpacas = new List<string>();
    
    public List<ObsidocClassExemple> classes = new List<ObsidocClassExemple>();
    public List<ObsidocInterfaceExemple> interfaces = new List<ObsidocInterfaceExemple>();
    
    public ObsidocEnumExemple enumExemple;
    private int _lamas;
    private string _alpaca;

    [ObsidocProperty("Nombre de lamas actifs, accessible en lecture seule depuis l'extérieur.")]
    public int LamaCount { get; private set; }

    [ObsidocProperty("Nom de l'alpaca courant.")]
    public string AlpacaName { get; set; }

    [ObsidocProperty("Liste des classes exemples associées, calculée à la volée.")]
    public List<ObsidocClassExemple> ActiveClasses { get; set; }

    [ObsidocProperty("Valeur d'enum courante.")]
    public ObsidocEnumExemple CurrentEnum { get; set; }

    private bool IsReady { get; set; }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    
    public void Test(int a, string b)
    {
        Debug.Log("Test");
    }

    public void Test()
    {
        
    }

    private void Test2(int a, string b)
    {
        Debug.Log("Test2");   
    }
    private void Test2()
    {
        
    }
}
