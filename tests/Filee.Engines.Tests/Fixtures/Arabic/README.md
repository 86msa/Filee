# Arabic shape references

Generated with Pillow 11.3.0 + libraqm (FriBidi/HarfBuzz), independently of the Filee PDF renderer.
Font: the exact SHA-256 verified static NotoSansArabic-Regular.ttf fetched by build/fetch-fonts.ps1.
48 pixels, direction rtl, language ar, baseline (1050,100), white 1100×160 RGB canvas.

Text samples in order:

السلام عليكم ورحمة الله وبركاته
بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ
لَا إلهَ إلّا الله، آلاء أحمد وإيمان

`libreoffice-original.pdf` was produced independently with LibreOffice 26.8 alpha from a DOCX containing the five required sample strings and explicit RTL paragraphs. It tests preservation of an existing PDF, including its original Unicode mappings (which are imperfect for vocalized text in this producer); Filee must not rewrite those mappings when merging/splitting/compressing.
