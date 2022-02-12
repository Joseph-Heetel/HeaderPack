# HeaderPack

Commandline tool written in C# for packing multiple C++ header/inline code files into one header file

## Usage

### 1. Put the headerpack.exe (with headerpack.dll) into the system PATH or your project directory
### 2. Create a file named `headerpackconfig.json` in your project root directory
### 3. Populate it:
  ```json
  {
    "include": "Relative path to a c++ code file",
    "output": "Relative path to the generated output file",
  }
  ```
### 4. Run headerpack.exe in your project root directory

The application will now recursively read through all included c++ code files starting with the one set in the config file. It will automatically sort them so the c++ compiler can make sense of it.

## Remarks
Link a c++ header or text file with the `"disclaimer"` Json property to have it print it at the top of your file. Useful for copyright notices.
  
