# 칸 감지 이벤트 (SRS 4.4). 박스당 1회 발행.
#   /detect_1_event : zone = reject | reject_wait
#   /detect_2_event : zone = end_1 | end_2 | end_wait
string box_id                 # BOX_001 형식
string zone
geometry_msgs/Point position  # ROS 좌표 (Unity에서 To<FLU>() 변환 후)
