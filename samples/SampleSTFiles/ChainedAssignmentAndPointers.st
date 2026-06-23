FUNCTION_BLOCK FB_Sample
VAR_INPUT
    p1 : POINTER TO ST_Sample;
END_VAR
VAR
    a       : ST_Sample;
    b       : ST_Sample;
    n       : DINT;
    nPrev   : DINT;
    ton1    : TON;
END_VAR
IF p1 <> 0 THEN
    (* chained assignment: write the source value into two targets at once *)
    a.f1 := b.g.f1 := p1^.f1;
    a.f2 := p1^.f2;
    MEMCPY(destAddr := ADR(a.buf), n := SIZEOF(a.buf), srcAddr := ADR(p1^.buf));
    ton1.IN := a.f2 = nPrev;
    ton1(PT := T#500MS);
    a.bActive := NOT ton1.Q;
    nPrev := a.f2;
ELSE
    p1^.f3 := b.f3 := 0;
END_IF
END_FUNCTION_BLOCK
