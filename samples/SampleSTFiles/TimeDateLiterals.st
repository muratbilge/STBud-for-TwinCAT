PROGRAM TimeLiteralShowcase
VAR
    tonDelay   : TON;
    tofDelay   : TOF;
    xTrigger   : BOOL;
    xDone      : BOOL;
    tCycle     : TIME := T#10MS;
    tLong      : TIME := TIME#1D2H3M4S5MS;
    dStart     : DATE := D#2026-01-15;
    todShift   : TOD := TOD#06:30:00;
    dtStamp    : DT := DT#2026-01-15-06:30:00.500;
    tArray     : ARRAY[1..3] OF TIME := [T#1S, T#2S, T#3S];
END_VAR
tonDelay(IN := xTrigger, PT := T#2S);
tofDelay(IN := tonDelay.Q, PT := T#500MS);
xDone := F_Compare(T#1S, tCycle);
IF tonDelay.ET > T#1.5S THEN
    tCycle := T#0S;
END_IF
CASE tCycle OF
    T#10MS:
        xDone := TRUE;
END_CASE
END_PROGRAM
