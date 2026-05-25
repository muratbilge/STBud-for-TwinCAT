PROGRAM MainProgram
    VAR
        counter : INT := 0;
        running : BOOL := FALSE;
        timer : TIME := T#0ms;
        message : STRING[80] := 'Hello';
        values : ARRAY[0..9] OF INT;
        fbMotor : FB_Motor;
    END_VAR

    IF running THEN
        counter := counter + 1;
        IF counter > 10 THEN
            running := FALSE;
            counter := 0;
        ELSIF counter > 5 THEN
            timer := T#500ms;
        ELSE
            timer := T#100ms;
        END_IF
    END_IF
    FOR i := 0 TO 9 DO
        values[i] := i * 2;
    END_FOR
    WHILE counter < 100 DO
        counter := counter + 1;
    END_WHILE
    REPEAT
        counter := counter - 1;
    UNTIL counter <= 0
    END_REPEAT
    CASE counter OF
        0: message := 'Zero';
        1 .. 5: message := 'Low';
        6, 7, 8: message := 'Medium';
    ELSE
        message := 'High';
    END_CASE
    fbMotor.Run(speed := 100);
END_PROGRAM
