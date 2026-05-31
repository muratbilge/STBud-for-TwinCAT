TYPE ST_MotorConfig :
STRUCT
    nominalSpeed:REAL;
    maxSpeed:REAL;
    acceleration:REAL;
    deceleration:REAL;
    direction:DINT;
    enabled:BOOL;
    name:STRING[32];
    position:ST_Point;
END_STRUCT
END_TYPE