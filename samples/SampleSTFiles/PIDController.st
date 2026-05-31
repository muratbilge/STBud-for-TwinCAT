FUNCTION_BLOCK FB_PDController
VAR_INPUT
    setpoint : REAL;
    actualValue : REAL;
    kp : REAL := 1.0;
    ki : REAL := 0.1;
    kd : REAL := 0.01;
    enable : BOOL;
END_VAR
VAR_OUTPUT
    output : REAL;
    error : REAL;
    saturated : BOOL;
END_VAR
VAR
    integral : REAL;
    prevError : REAL;
    dt : TIME := T#10ms;
    maxOutput : REAL := 100.0;
    minOutput : REAL := -100.0;
END_VAR

IF enable THEN
    error := setpoint - actualValue;
    integral := integral + error * TIME_TO_REAL(dt);
    output := kp * error + ki * integral + kd * (error - prevError) / TIME_TO_REAL(dt);
    prevError := error;
    IF output > maxOutput THEN
        output := maxOutput;
        saturated := TRUE;
    ELSIF output < minOutput THEN
        output := minOutput;
        saturated := TRUE;
    ELSE
        saturated := FALSE;
    END_IF
ELSE
    output := 0.0;
    integral := 0.0;
    prevError := 0.0;
    error := 0.0;
    saturated := FALSE;
END_IF

END_FUNCTION_BLOCK