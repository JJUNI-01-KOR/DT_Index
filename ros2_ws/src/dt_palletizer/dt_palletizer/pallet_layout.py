"""팔레트 슬롯 번호 -> 놓을 위치. 담당 D · scope `palletizer`

출고 팔레트(palletizer_node)와 불량 팔레트(factory_manager)가 같이 쓴다.
slot_pose의 이름과 인자를 바꾸면 B에게 먼저 알린다.
"""
import math


def slot_pose(n, origin, pitch, slot_z, cols=2, rows=2, layers=3):
    """n번째 슬롯(1부터)의 놓을 위치를 돌려준다.

    origin: (x, y, z, yaw) 팔레트 중심, ROS 좌표
    pitch:  (Px, Py) 칸 간격 m
    slot_z: 한 층 높이 m (박스 높이)
    반환:   (x, y, z, qz, qw)  z축 회전만 있는 쿼터니언. z는 박스 중심 높이
    """
    per_layer = cols * rows
    if not 1 <= n <= per_layer * layers:
        raise ValueError(f'slot {n} out of range')
    layer, k = divmod(n - 1, per_layer)
    row, col = divmod(k, cols)
    dx = (col - (cols - 1) / 2) * pitch[0]
    dy = (row - (rows - 1) / 2) * pitch[1]
    dz = layer * slot_z + slot_z / 2
    ox, oy, oz, yaw = origin
    c, s = math.cos(yaw), math.sin(yaw)
    return (ox + c * dx - s * dy,
            oy + s * dx + c * dy,
            oz + dz,
            math.sin(yaw / 2),
            math.cos(yaw / 2))
