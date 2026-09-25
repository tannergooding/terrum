; Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License.
; Deliberately preserve unspecified upper argument bits for narrow ABI probes.
PUBLIC CallWithCarrier
.code
CallWithCarrier PROC FRAME
    sub rsp, 40
    .allocstack 40
    .endprolog
    mov rax, rcx
    mov rcx, rdx
    call rax
    add rsp, 40
    ret
CallWithCarrier ENDP
END
