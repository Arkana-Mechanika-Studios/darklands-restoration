Roland MT-32 and CM-32L ROM files go here, if you have them. With them the game plays its music and sounds as on a Roland
MT-32 or CM-32L (the launcher's Sound row); without them it plays them on the OPL3 (AdLib / Sound Blaster) drivers instead.

No ROM files are included: they are Roland's, and you need your own copies (dumped from a module you own).

Each module needs two files, its control ROM and its PCM ROM, named the usual way, for example

  MT32_CONTROL.ROM and MT32_PCM.ROM      (MT-32)
  CM32L_CONTROL.ROM and CM32L_PCM.ROM    (CM-32L)

The game takes any .rom file whose name has "ctrl" or "control" in it for the control ROM and "pcm" for the PCM ROM; a name
with "cm32l" or "cm-32l" in it is a CM-32L file. Put them straight in this folder, or in a subfolder of their own named MT32
or CM32L. The launcher shows which of the two it found. Another folder can be chosen in the launcher or in settings.json
(docs/SETTINGS.md: sound.mt32Roms).

Unpacking a new version of the game over this one leaves this folder alone.
