format PE64 GUI 5.0 DLL
entry DllEntryPoint

include 'win64a.inc'

section '.text' code readable executable

proc DllEntryPoint hinstDLL, fdwReason, lpvReserved
	mov	eax, TRUE
	ret
endp

proc ReadTimeStampCounter
	rdtsc
	shl	rdx, 32
	or	rax, rdx
	mov	qword [rcx], rax
	ret
endp

data fixups
end data

section '.edata' export data readable

export 'RDTSC64.DLL',\
	 ReadTimeStampCounter,'ReadTimeStampCounter'