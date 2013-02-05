format PE GUI 4.0 DLL
entry DllEntryPoint

include 'win32a.inc'

section '.text' code readable executable

proc DllEntryPoint hinstDLL, fdwReason, lpvReserved
	mov	eax, TRUE
	ret
endp

proc ReadTimeStampCounter
	mov	ecx, dword [esp + 4]
	rdtsc
	mov	dword [ecx], eax
	mov	dword [ecx + 4], edx
	ret	4
endp

data fixups
end data

section '.edata' export data readable

export 'RDTSC32.DLL',\
	 ReadTimeStampCounter,'ReadTimeStampCounter'