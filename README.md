# CodeTrivia
Visual Studio extension to extract basic code information.
---
Commands
1. Get Usings Trivia: Returns to the Windows clipboard the number of projects scanned, the number of syntax trees scanned, and raw count of symbols used in the solution grouped by namespace (excluding the symbols in the actual "using ..." statements themselves).  The idea is to give a sense of what libraries the solution is actually using and how heavily they're used relative to each other.

2. Get Composition Trivia: Reuturns to the Windows clipboard an XML containing the projects scanned, the types, and the type references found in all types.
