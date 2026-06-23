# Register the application component.
#
# We gather every C/C++ source under src/ (recursively, so the per-feature
# sub-folders are picked up automatically) and expose "src" as an include root.
# That lets headers be included by their folder-qualified path, e.g.
#     #include "gnss/GnssModule.h"
# which keeps includes unambiguous and self-documenting.

FILE(GLOB_RECURSE app_sources ${CMAKE_SOURCE_DIR}/src/*.c ${CMAKE_SOURCE_DIR}/src/*.cpp)

idf_component_register(
    SRCS ${app_sources}
    INCLUDE_DIRS "."
    REQUIRES driver
)
