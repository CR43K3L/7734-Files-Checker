using Xunit;

// Certains tests changent l'unité d'affichage globale : on les exécute l'un après l'autre.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
