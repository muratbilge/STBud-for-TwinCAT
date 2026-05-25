FUNCTION_BLOCK FB_Motor
VAR_INPUT
    speed : INT;
    enable : BOOL;
END_VAR
VAR_OUTPUT
    running : BOOL;
END_VAR
VAR
    internalCounter : INT;
END_VAR

IF enable THEN
    running := TRUE;
    internalCounter := internalCounter + speed;
ELSE
    running := FALSE;
    internalCounter := 0;
END_IF;

END_FUNCTION_BLOCK
