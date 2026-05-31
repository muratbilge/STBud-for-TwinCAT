TYPE ST_MotorData :
STRUCT
    pPosition:POINTER TO LREAL;
    refVelocity:REF_TO REAL;
    name:STRING[64];
    trajectory:ARRAY[0..99] OF REAL;
END_STRUCT
END_TYPE